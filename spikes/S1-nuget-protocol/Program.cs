using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

// S1: can NuGet.Protocol answer the range questions, and where does the HTTP seam sit?
const string Nuget = "https://api.nuget.org/v3/index.json";
const string Dummy = "http://127.0.0.1:9/v3/index.json"; // discard port, nothing listens, nothing leaves the machine
var cannedDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "canned");
cannedDir = Path.GetFullPath(cannedDir);
var tmp = Directory.CreateTempSubdirectory("s1-").FullName;
// NuGet caches the service index on disk (40 min TTL) below the handler, so a warm machine-wide cache hides requests from the seam.
// A cold, private HTTP cache makes the handler see everything; the real tool must pin this too (offline guard, design 7.5).
Environment.SetEnvironmentVariable("NUGET_HTTP_CACHE_PATH", Path.Combine(tmp, "http-cache"));
var fails = new List<string>();
void Check(string name, bool ok) { Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}"); if (!ok) fails.Add(name); }

try
{
    // ---- Steps 1-3, with a counting handler in the seam (Step 4a) ----
    var counter = new Counting(Plain());
    var settings = MakeSettings(Path.Combine(tmp, "one"), mapping: false);
    var repo = Repo(settings, "nuget", counter);
    var meta = await repo.GetResourceAsync<PackageMetadataResource>();
    var log = NuGet.Common.NullLogger.Instance;
    using var cache = new SourceCacheContext { NoCache = true };

    Console.WriteLine("== Step 1: Contentment registration ==");
    var contentment = (await meta.GetMetadataAsync("Umbraco.Community.Contentment", includePrerelease: true, includeUnlisted: false, cache, log, CancellationToken.None)).ToList();
    foreach (var m in contentment.OrderBy(m => m.Identity.Version))
        Console.WriteLine($"  {m.Identity.Version,-14} " + string.Join(" | ", m.DependencySets.Select(g => $"{g.TargetFramework.GetShortFolderName()}: " + string.Join(", ", g.Packages.Where(p => p.Id.StartsWith("Umbraco.Cms", StringComparison.OrdinalIgnoreCase)).Select(p => $"{p.Id} {p.VersionRange}")))));
    Check("registration returned versions with dependency groups", contentment.Count > 0 && contentment.Any(m => m.DependencySets.Any()));

    Console.WriteLine("== Step 2: group choice for Umbraco.Cms target TFM ==");
    var cms = (await meta.GetMetadataAsync("Umbraco.Cms", false, false, cache, log, CancellationToken.None)).ToList();
    var cmsVersion = NuGetVersion.Parse("17.7.2");
    var cmsPick = cms.FirstOrDefault(m => m.Identity.Version == cmsVersion);
    if (cmsPick is null) { cmsPick = cms.Where(m => m.Identity.Version.Major == 17).OrderByDescending(m => m.Identity.Version).First(); Console.WriteLine($"  17.7.2 NOT FOUND, using {cmsPick.Identity.Version}"); }
    var target = cmsPick.Identity.Version;
    var cmsGroups = cmsPick.DependencySets.Select(g => g.TargetFramework).ToList();
    Console.WriteLine($"  Umbraco.Cms {target} groups: {string.Join(", ", cmsGroups.Select(g => g.GetShortFolderName()))}");
    var targetTfm = cmsGroups.OrderByDescending(g => g.Version).First(); // the site's TFM, read from Cms's own groups
    Console.WriteLine($"  target TFM = {targetTfm.GetShortFolderName()}");
    var step2 = new Dictionary<string, bool>();
    foreach (var v in new[] { "6.2.1", "6.3.3" })
    {
        var m = contentment.Single(x => x.Identity.Version == NuGetVersion.Parse(v));
        var group = NuGetFrameworkUtility.GetNearest(m.DependencySets, targetTfm, g => g.TargetFramework);
        var deps = group?.Packages.Where(p => p.Id.StartsWith("Umbraco.Cms", StringComparison.OrdinalIgnoreCase)).ToList() ?? new();
        Console.WriteLine($"  {v}: nearest group {group?.TargetFramework.GetShortFolderName() ?? "(none)"}");
        foreach (var d in deps) Console.WriteLine($"    {d.Id} {d.VersionRange} allows {target}: {d.VersionRange.Satisfies(target)}");
        step2[v] = deps.Count > 0 && deps.All(d => d.VersionRange.Satisfies(target));
    }
    Check("6.2.1 does not clear target", !step2["6.2.1"]);
    Check("6.3.3 clears target", step2["6.3.3"]);

    // Mechanism check, independent of the live data: the same code must also say "no" when a range excludes the target.
    var clearing = contentment.Where(m => !m.Identity.Version.IsPrerelease).Where(m =>
    {
        var g = NuGetFrameworkUtility.GetNearest(m.DependencySets, targetTfm, x => x.TargetFramework);
        var d = g?.Packages.Where(p => p.Id.StartsWith("Umbraco.Cms", StringComparison.OrdinalIgnoreCase)).ToList();
        return d is { Count: > 0 } && d.All(p => p.VersionRange.Satisfies(target));
    }).Select(m => m.Identity.Version).OrderBy(v => v).ToList();
    Console.WriteLine($"  (supplementary) stable Contentment versions whose Umbraco.Cms* ranges allow {target}: {clearing.First()} .. {clearing.Last()} ({clearing.Count} versions)");
    var against18 = NuGetVersion.Parse("18.0.0");
    var m621 = contentment.Single(x => x.Identity.Version == NuGetVersion.Parse("6.2.1"));
    var r621 = NuGetFrameworkUtility.GetNearest(m621.DependencySets, targetTfm, x => x.TargetFramework)!.Packages.First(p => p.Id == "Umbraco.Cms.Core").VersionRange;
    Console.WriteLine($"  (supplementary) 6.2.1 {r621} allows 18.0.0: {r621.Satisfies(against18)}");
    Check("(mechanism) range maths rejects an out-of-range target", !r621.Satisfies(against18));

    Console.WriteLine("== Step 3: lowest-major-highest-patch over stable > 6.2.1 ==");
    var stable = contentment.Select(m => m.Identity.Version).Where(v => !v.IsPrerelease && v > NuGetVersion.Parse("6.2.1")).ToList();
    var lowestMajor = stable.Min(v => v.Major);
    var pick = stable.Where(v => v.Major == lowestMajor).Max()!;
    var highest = stable.Max()!;
    Console.WriteLine($"  pick = {pick}, highest overall = {highest}");
    Check("pick is 6.3.3", pick.ToNormalizedString() == "6.3.3");
    Check("highest overall is 7.x", highest.Major == 7);

    Console.WriteLine("== Step 4: HTTP seam ==");
    Console.WriteLine($"  counted requests for Steps 1-3: {counter.Count}");
    foreach (var u in counter.Urls.Take(6)) Console.WriteLine($"    {u}");
    Check("counting handler saw requests", counter.Count > 0);

    // Record then replay: canned files stand in for the network.
    Directory.CreateDirectory(cannedDir);
    foreach (var f in Directory.GetFiles(cannedDir)) File.Delete(f);
    ColdCache();
    var recorder = new Recording(new Counting(Plain()), cannedDir);
    var recRepo = Repo(MakeSettings(Path.Combine(tmp, "rec"), false), "nuget", recorder);
    var recMeta = await recRepo.GetResourceAsync<PackageMetadataResource>();
    var recorded = (await recMeta.GetMetadataAsync("Umbraco.Community.Contentment", true, false, cache, log, CancellationToken.None)).Count();
    Console.WriteLine($"  recorded {Directory.GetFiles(cannedDir).Length} canned file(s), {Directory.GetFiles(cannedDir).Sum(f => new FileInfo(f).Length)} bytes");

    ColdCache();
    var canned = new Canned(cannedDir);
    var offlineRepo = Repo(MakeSettings(Path.Combine(tmp, "off"), false), "nuget", canned);
    var offMeta = await offlineRepo.GetResourceAsync<PackageMetadataResource>();
    var replayed = (await offMeta.GetMetadataAsync("Umbraco.Community.Contentment", true, false, cache, log, CancellationToken.None)).ToList();
    Console.WriteLine($"  replayed from files: {replayed.Count} versions, canned responses served: {canned.Served}, misses: {canned.Misses}");
    Check("canned handler served at least one response", canned.Served > 0);
    var recordedVersions = (await recMeta.GetMetadataAsync("Umbraco.Community.Contentment", true, false, cache, log, CancellationToken.None)).Select(m => m.Identity.Version).OrderBy(v => v).ToList();
    var sameVersions = recordedVersions.SequenceEqual(replayed.Select(m => m.Identity.Version).OrderBy(v => v));
    Check("replay returns the same version list as the recording and no real network", recorded == replayed.Count && sameVersions && canned.Misses == 0);

    Console.WriteLine("== Step 5: source mapping with a dummy second feed ==");
    var dummyCounter = new Counting(Plain());
    var nugetCounter = new Counting(Plain());
    var mapSettings = MakeSettings(Path.Combine(tmp, "map"), mapping: true);
    var sources = SettingsUtility.GetEnabledSources(mapSettings).ToList();
    Console.WriteLine($"  enabled sources: {string.Join(", ", sources.Select(s => s.Name))}");
    var mapping = PackageSourceMapping.GetPackageSourceMapping(mapSettings);
    var feeds = mapping.GetConfiguredPackageSources("Umbraco.Community.Contentment").ToList();
    Console.WriteLine($"  mapping for Umbraco.Community.Contentment: {string.Join(", ", feeds)}");
    // NuGet.Protocol does not apply mapping when you build repositories yourself; we pick feeds from it.
    foreach (var s in sources)
    {
        var h = s.Name == "dummy" ? dummyCounter : nugetCounter;
        if (!feeds.Contains(s.Name, StringComparer.OrdinalIgnoreCase)) continue;
        var r = Repo(mapSettings, s.Name, h);
        var mr = await r.GetResourceAsync<PackageMetadataResource>();
        await mr.GetMetadataAsync("Umbraco.Community.Contentment", true, false, cache, log, CancellationToken.None);
    }
    Console.WriteLine($"  requests: nuget.org={nugetCounter.Count}, dummy={dummyCounter.Count}");
    Check("mapped feed contacted, dummy feed never", nugetCounter.Count > 0 && dummyCounter.Count == 0);
    // Control: prove the dummy counter can see traffic, so zero above means something. Connection is refused locally.
    try { await (await Repo(mapSettings, "dummy", dummyCounter).GetResourceAsync<PackageMetadataResource>()).GetMetadataAsync("Umbraco.Community.Contentment", true, false, cache, log, CancellationToken.None); }
    catch (Exception e) { Console.WriteLine($"  control: direct call to dummy refused as expected ({e.GetType().Name})"); }
    Check("(control) counter registers a request when the dummy is queried directly", dummyCounter.Count > 0);
}
finally { try { Directory.Delete(tmp, true); } catch { } }

Console.WriteLine(fails.Count == 0 ? "RESULT: ALL PASS" : "RESULT: FAIL -> " + string.Join("; ", fails));
return fails.Count == 0 ? 0 : 1;

// NuGet's own handler turns on decompression; a replacement inner handler must too or the JSON arrives gzipped.
static HttpClientHandler Plain() => new() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };

void ColdCache() { try { Directory.Delete(Path.Combine(tmp, "http-cache"), true); } catch (DirectoryNotFoundException) { } }

static ISettings MakeSettings(string dir, bool mapping)
{
    Directory.CreateDirectory(dir);
    var xml = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget" value="{Nuget}" />
        {(mapping ? $"    <add key=\"dummy\" value=\"{Dummy}\" allowInsecureConnections=\"true\" />" : "")}
          </packageSources>
        {(mapping ? """
          <packageSourceMapping>
            <packageSource key="nuget"><package pattern="Umbraco.*" /></packageSource>
            <packageSource key="dummy"><package pattern="Internal.*" /></packageSource>
          </packageSourceMapping>
        """ : "")}
        </configuration>
        """;
    File.WriteAllText(Path.Combine(dir, "NuGet.config"), xml);
    return Settings.LoadDefaultSettings(dir, configFileName: null, machineWideSettings: null);
}

// The seam: swap NuGet's HttpHandlerResourceV3Provider for one whose message handler we control.
static SourceRepository Repo(ISettings settings, string sourceName, HttpMessageHandler handler)
{
    var source = SettingsUtility.GetEnabledSources(settings).Single(s => s.Name == sourceName);
    var providers = Repository.Provider.GetCoreV3().Where(p => p.Value.ResourceType != typeof(HttpHandlerResource)).ToList();
    providers.Add(new Lazy<INuGetResourceProvider>(() => new SeamProvider(handler)));
    return new SourceRepository(source, providers);
}

sealed class SeamProvider(HttpMessageHandler inner) : ResourceProvider(typeof(HttpHandlerResource), nameof(SeamProvider), NuGetResourceProviderPositions.Last)
{
    public override Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
    {
        var res = new HttpHandlerResourceV3(new HttpClientHandler(), inner);
        return Task.FromResult(new Tuple<bool, INuGetResource?>(true, res));
    }
}

class Counting(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    readonly ConcurrentQueue<string> urls = new();
    public int Count => urls.Count;
    public IEnumerable<string> Urls => urls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { urls.Enqueue(request.RequestUri!.ToString()); return base.SendAsync(request, ct); }
}

static class Key { public static string For(Uri u) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(u.ToString())))[..16]; }

sealed class Recording(HttpMessageHandler inner, string dir) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var res = await base.SendAsync(request, ct);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        var k = Key.For(request.RequestUri!);
        File.WriteAllText(Path.Combine(dir, k + ".url"), request.RequestUri!.ToString());
        File.WriteAllBytes(Path.Combine(dir, k + ".body"), bytes);
        var copy = new HttpResponseMessage(res.StatusCode) { Content = new ByteArrayContent(bytes) };
        copy.Content.Headers.ContentType = res.Content.Headers.ContentType;
        return copy;
    }
}

sealed class Canned(string dir) : HttpMessageHandler
{
    public int Served, Misses;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var f = Path.Combine(dir, Key.For(request.RequestUri!) + ".body");
        if (!File.Exists(f)) { Interlocked.Increment(ref Misses); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
        Interlocked.Increment(ref Served);
        var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(f)) };
        res.Content.Headers.ContentType = new("application/json");
        return Task.FromResult(res);
    }
}
