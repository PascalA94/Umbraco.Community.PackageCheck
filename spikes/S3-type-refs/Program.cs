// Spike S3 (plan Task 4): can H09 be decided from type references read with System.Reflection.Metadata
// over bytes from the nupkg zip stream, without loading any package assembly?
// Hostile-input rules (design section 4): no Assembly.Load*/AssemblyLoadContext/MetadataLoadContext on package
// content, no extraction to disk, unsafe entry names rejected, byte-counting reads with per-entry and total caps.
// The nupkg file itself sits in a temp dir outside the repo (passed as args[0]); entries are only ever read in memory.
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using NuGet.Versioning;

const long NupkgCap = 250L * 1024 * 1024;
const long PerEntryCap = 50L * 1024 * 1024;
const long TotalCap = 500L * 1024 * 1024;
const int EntryCountCap = 20_000;
const string H09Namespace = "Umbraco.Cms.Infrastructure.Manifest";
string[] h09Names = ["IPackageManifestReader", "PackageManifest"];
var target = NuGetVersion.Parse("17.7.2");

if (args.Length < 1 || !Directory.Exists(args[0])) { Console.Error.WriteLine("usage: S3 <temp-dir-outside-repo>"); return 2; }
var tempDir = args[0];

// Snapshot before any package bytes are touched, so the no-load check can tell runtime loads from package loads.
var loadedBefore = LoadedNames();

using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("umbraco-package-check-spike-s3/0.0 (+https://github.com/PascalA94/Umbraco.Community.PackageCheck)");

var regCache = new Dictionary<string, List<RegEntry>>(StringComparer.OrdinalIgnoreCase);
var scanCache = new Dictionary<string, Scan>(StringComparer.OrdinalIgnoreCase);
var umbPathCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
var scannedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var dllCount = 0;

// ---------- Step 1: SimpleDashboards and jcdcdev.Umbraco.Core ----------
Console.WriteLine("## Step 1: SimpleDashboards 17.2.0 and its jcdcdev.Umbraco.Core");
var sd = await ScanPackage("Umbraco.Community.SimpleDashboards", "17.2.0");
var sdDeps = await UmbracoDeps("Umbraco.Community.SimpleDashboards", "17.2.0");
var step1Found = sd.H09Refs.Count > 0;
PrintScan(sd);
foreach (var (depId, depVer, range) in sdDeps)
{
    var ds = await ScanPackage(depId, depVer);
    Console.WriteLine($"  dependency {depId} range {range} -> resolved {depVer}");
    PrintScan(ds);
    step1Found |= ds.H09Refs.Count > 0;
}
Console.WriteLine($"Step 1 H09 found (SimpleDashboards or its Umbraco deps): {step1Found}");

// ---------- Step 2: control set ----------
Console.WriteLine();
Console.WriteLine("## Step 2: control set");
var workflowStatic = (await UmbracoDeps("Umbraco.Workflow", "17.5.4")).FirstOrDefault(d => d.Id.Equals("Umbraco.Workflow.StaticAssets", StringComparison.OrdinalIgnoreCase));
(string Id, string? Version)[] controls =
[
    ("astroboard", "1.1.3"),
    ("Umbraco.Workflow", "17.5.4"),
    ("Umbraco.Workflow.StaticAssets", workflowStatic.Version),
    ("Crumpled.RobotsTxt", "3.1.1"),
    ("MediaWiz.Forums", "14.0.0"),
    ("Umbraco.Community.Contentment", "6.3.3"),
    ("Skybrud.Umbraco.Redirects", "17.0.5"),
    ("Our.Umbraco.GMaps", "17.3.1"),
    ("Umbraco.Community.BlockPreview", "5.6.0"),
    ("Umbraco.Forms", null),
    ("uSync", null),
    ("Umbraco.Commerce", null),
];
var reserves = new Queue<string>(["Umbraco.Deploy", "Diplo.GodMode"]);
var rows = new List<Row>();
foreach (var (cid, cver) in controls)
{
    var id = cid; var ver = cver; string? swap = null;
    if (ver is null)
    {
        var entries = await Registration(id);
        if (entries.Count == 0 && reserves.Count > 0) { swap = $"{id} not on nuget.org, swapped for "; id = reserves.Dequeue(); swap += id; }
        ver = await LatestAllowing(id);
        if (ver is null) { rows.Add(new Row(id, "(none)", false, false, "no stable version allows 17.7.2", false, false, 0, swap, null)); continue; }
    }
    else if ((await Registration(id)).All(e => !e.Version.Equals(NuGetVersion.Parse(ver))))
    {
        if (reserves.Count == 0) { rows.Add(new Row(id, ver, false, false, "version not on nuget.org, no reserve left", false, false, 0, null, null)); continue; }
        swap = $"{id} {ver} not on nuget.org, swapped for "; id = reserves.Dequeue(); ver = await LatestAllowing(id); swap += $"{id} {ver}";
        if (ver is null) continue;
    }

    var own = await ScanPackage(id, ver);
    var deps = await UmbracoDeps(id, ver);
    var depScans = new List<Scan>();
    foreach (var d in deps) depScans.Add(await ScanPackage(d.Id, d.Version));

    var depWithUpj = depScans.Where(s => s.UmbracoPackageJson.Count > 0).Select(s => $"{s.Id} {s.Version}").ToList();
    var hasClient = own.Js.Count > 0 || own.PackageManifest.Count > 0;
    string why; bool working;
    if (own.UmbracoPackageJson.Count > 0) { working = true; why = $"own umbraco-package.json ({own.UmbracoPackageJson[0]})"; }
    else if (depWithUpj.Count > 0) { working = true; why = $"depth-1 dep ships umbraco-package.json ({string.Join(", ", depWithUpj)})"; }
    else if (!hasClient) { working = true; why = "no client assets (no js/mjs, no package.manifest)"; }
    else { working = false; why = $"client assets present ({own.Js.Count} js/mjs, {own.PackageManifest.Count} package.manifest), no umbraco-package.json at depth 0-1"; }

    // Plan instruction: note whether depth 2 would change the row. Only a "not working" row can change.
    string? depth2 = null;
    if (!working)
    {
        var d2 = new List<string>();
        foreach (var d in deps)
            foreach (var dd in await UmbracoDeps(d.Id, d.Version))
            {
                var s2 = await ScanPackage(dd.Id, dd.Version);
                if (s2.UmbracoPackageJson.Count > 0) d2.Add($"{dd.Id} {dd.Version}");
            }
        depth2 = d2.Count > 0 ? $"depth 2 WOULD change: {string.Join(", ", d2)}" : "depth 2 does not change";
    }
    else if (!own.UmbracoPackageJson.Any() && depWithUpj.Count == 0) depth2 = "n/a (no client assets)";

    var h09Own = own.H09Refs.Count > 0;
    var h09Dep = depScans.Any(s => s.H09Refs.Count > 0);
    var mgmt = own.MgmtRefs.Count + depScans.Sum(s => s.MgmtRefs.Count);
    rows.Add(new Row(id, ver, working, true, why, h09Own, h09Dep, mgmt, swap, depth2)
    {
        Deps = deps.Select(d => $"{d.Id} {d.Version}").ToList(),
        H09Detail = own.H09Refs.Select(r => $"{own.Id}: {r}").Concat(depScans.SelectMany(s => s.H09Refs.Select(r => $"{s.Id}: {r}"))).Distinct().ToList(),
        OtherManifestRefs = own.OtherManifestRefs.Concat(depScans.SelectMany(s => s.OtherManifestRefs)).Distinct().ToList(),
        Implementers = own.Implementers.Select(t => $"{own.Id}: {t}").Concat(depScans.SelectMany(s => s.Implementers.Select(t => $"{s.Id}: {t}"))).Distinct().ToList(),
    });
}

Console.WriteLine("| # | id | version | working | why | H09 own | H09 via dep | Mgmt API refs (own+deps) | depth 2 | swap |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
var i = 0;
foreach (var r in rows)
    Console.WriteLine($"| {++i} | {r.Id} | {r.Version} | {(r.Working ? "yes" : "no")} | {r.Why} | {(r.H09Own ? "yes" : "no")} | {(r.H09Dep ? "yes" : "no")} | {r.Mgmt} | {r.Depth2 ?? "-"} | {r.Swap ?? "-"} |");
foreach (var r in rows)
{
    Console.WriteLine($"- {r.Id} {r.Version}: Umbraco deps [{string.Join(", ", r.Deps)}]");
    foreach (var h in r.H09Detail) Console.WriteLine($"    H09 ref: {h}");
    foreach (var h in r.OtherManifestRefs) Console.WriteLine($"    other manifest-named ref: {h}");
    foreach (var h in r.Implementers) Console.WriteLine($"    implements IPackageManifestReader: {h}");
}
var falsePositives = rows.Where(r => (r.H09Own || r.H09Dep) && !r.Working).ToList();
Console.WriteLine($"False positives (H09 flag and not working): {falsePositives.Count} {string.Join(", ", falsePositives.Select(r => r.Id))}");
// Not the plan's definition: shown so the controller can see whether each false positive registers its manifest in code.
foreach (var fp in falsePositives)
    Console.WriteLine($"  {fp.Id}: implements IPackageManifestReader = {(fp.Implementers.Count > 0 ? string.Join("; ", fp.Implementers) : "no")}");

// ---------- Step 3: assert no load ----------
Console.WriteLine();
Console.WriteLine("## Step 3: no-load check");
var loadedAfter = LoadedNames();
var hits = loadedAfter.Where(scannedAssemblyNames.Contains).OrderBy(n => n, StringComparer.Ordinal).ToList();
var newSinceStart = loadedAfter.Except(loadedBefore, StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal).ToList();
var contexts = AssemblyLoadContext.All.Select(c => c.Name ?? "(unnamed)").ToList();
// Self-test of the comparison: a name we know is loaded must be found by the same lookup.
var probe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System.Reflection.Metadata" };
var probeHit = loadedAfter.Any(probe.Contains);
Console.WriteLine($"DLLs scanned: {dllCount}; distinct scanned simple names: {scannedAssemblyNames.Count}");
Console.WriteLine($"Loaded assemblies before: {loadedBefore.Count}, after: {loadedAfter.Count}");
Console.WriteLine($"New since start: {string.Join(", ", newSinceStart)}");
Console.WriteLine($"AssemblyLoadContexts: {string.Join(", ", contexts)}");
Console.WriteLine($"Probe (System.Reflection.Metadata detected by same comparison): {probeHit}");
Console.WriteLine($"Scanned names found loaded: {(hits.Count == 0 ? "none" : string.Join(", ", hits))}");
Console.WriteLine($"Scanned names: {string.Join(", ", scannedAssemblyNames.OrderBy(n => n, StringComparer.Ordinal))}");

var noLoad = hits.Count == 0 && probeHit && contexts.Count == 1;
var go = step1Found && falsePositives.Count == 0 && noLoad;
Console.WriteLine();
Console.WriteLine($"RESULT: step1Found={step1Found} falsePositives={falsePositives.Count} noLoad={noLoad} => {(go ? "GO (H09 high)" : "NO-GO")}");
return hits.Count > 0 ? 3 : 0;

// ---------------- helpers ----------------

static HashSet<string> LoadedNames() =>
    AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

void PrintScan(Scan s)
{
    Console.WriteLine($"- {s.Id} {s.Version}: {s.Dlls.Count} lib dll(s) [{string.Join(", ", s.Dlls)}]");
    Console.WriteLine($"    H09 refs: {(s.H09Refs.Count == 0 ? "none" : string.Join("; ", s.H09Refs))}");
    Console.WriteLine($"    Infrastructure.Manifest refs: {(s.ManifestNsRefs.Count == 0 ? "none" : string.Join("; ", s.ManifestNsRefs))}");
    Console.WriteLine($"    implements IPackageManifestReader: {(s.Implementers.Count == 0 ? "none" : string.Join("; ", s.Implementers))}");
    Console.WriteLine($"    Management API refs ({s.MgmtRefs.Count}): {string.Join("; ", s.MgmtRefs.Take(12))}{(s.MgmtRefs.Count > 12 ? "; ..." : "")}");
}

async Task<string> GetString(string url)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var resp = await http.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.NotFound) return "";
            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500) throw new HttpRequestException($"{resp.StatusCode}");
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException) when (attempt < 3) { await Task.Delay(1000 * attempt); }
    }
}

async Task<List<RegEntry>> Registration(string id)
{
    if (regCache.TryGetValue(id, out var cached)) return cached;
    var list = new List<RegEntry>();
    var body = await GetString($"https://api.nuget.org/v3/registration5-gz-semver2/{id.ToLowerInvariant()}/index.json");
    if (body.Length > 0)
    {
        var root = JsonNode.Parse(body)!;
        foreach (var page in root["items"]!.AsArray())
        {
            var items = page!["items"]?.AsArray();
            if (items is null) items = JsonNode.Parse(await GetString(page["@id"]!.GetValue<string>()))!["items"]!.AsArray();
            foreach (var leaf in items)
            {
                var ce = leaf!["catalogEntry"]!;
                var groups = new List<(string Tfm, List<(string Id, string Range)> Deps)>();
                foreach (var g in ce["dependencyGroups"]?.AsArray() ?? [])
                    groups.Add((g!["targetFramework"]?.GetValue<string>() ?? "",
                        (g["dependencies"]?.AsArray() ?? []).Select(d => (d!["id"]!.GetValue<string>(), d["range"]?.GetValue<string>() ?? "")).ToList()));
                list.Add(new RegEntry(NuGetVersion.Parse(ce["version"]!.GetValue<string>()), ce["listed"]?.GetValue<bool>() ?? true, groups));
            }
        }
    }
    return regCache[id] = list;
}

// Nearest group for net10.0, simplified: exact, then highest netN.0 <= 10, then netstandard/netcoreapp, then the TFM-less group.
static List<(string Id, string Range)> PickGroup(RegEntry e)
{
    static double Rank(string tfm)
    {
        var t = tfm.ToLowerInvariant().TrimStart('.');
        if (t.Length == 0) return 0.1;
        if (t.StartsWith("netstandard")) return 0.5;
        if (t.StartsWith("netcoreapp")) return 0.6;
        if (t.StartsWith("net") && double.TryParse(t[3..].Split('-')[0], System.Globalization.CultureInfo.InvariantCulture, out var v) && t.Contains('.'))
            return v <= 10.0 ? v : -1;
        return -1; // .NETFramework etc. do not apply to a net10.0 site
    }
    if (e.Groups.Count == 0) return [];
    var best = e.Groups.Select(g => (g, r: Rank(g.Tfm))).Where(x => x.r >= 0).OrderByDescending(x => x.r).FirstOrDefault();
    return best.g.Deps ?? [];
}

static VersionRange Range(string r) => VersionRange.Parse(string.IsNullOrWhiteSpace(r) ? "0.0.0" : r);
static bool IsCms(string id) => id.StartsWith("Umbraco.Cms", StringComparison.OrdinalIgnoreCase);
static bool IsPlatform(string id) => id.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("NETStandard", StringComparison.OrdinalIgnoreCase);

async Task<string?> Resolve(string id, string range)
{
    var vr = Range(range);
    var entries = await Registration(id);
    // NuGet's lowest-applicable rule; prefer a listed version.
    return (entries.Where(e => e.Listed && vr.Satisfies(e.Version)).OrderBy(e => e.Version).FirstOrDefault()
         ?? entries.Where(e => vr.Satisfies(e.Version)).OrderBy(e => e.Version).FirstOrDefault())?.Version.ToNormalizedString();
}

// Does this package reach Umbraco.Cms.* within 3 dependency hops? (Definition of "Umbraco package" for the depth walk.)
async Task<bool> HasUmbracoPath(string id, string version, int hops = 3)
{
    var key = $"{id}/{version}/{hops}";
    if (umbPathCache.TryGetValue(key, out var c)) return c;
    var e = (await Registration(id)).FirstOrDefault(x => x.Version.Equals(NuGetVersion.Parse(version)));
    var result = false;
    if (e is not null)
    {
        var deps = PickGroup(e);
        if (deps.Any(d => IsCms(d.Id))) result = true;
        else if (hops > 1)
            foreach (var d in deps.Where(d => !IsPlatform(d.Id)))
            {
                var v = await Resolve(d.Id, d.Range);
                if (v is not null && await HasUmbracoPath(d.Id, v, hops - 1)) { result = true; break; }
            }
    }
    return umbPathCache[key] = result;
}

// Direct dependencies (depth 1) that are Umbraco packages, excluding Umbraco.Cms.* itself (core ships its own
// backoffice manifests, which would make every package "working").
async Task<List<(string Id, string Version, string Range)>> UmbracoDeps(string id, string version)
{
    var e = (await Registration(id)).First(x => x.Version.Equals(NuGetVersion.Parse(version)));
    var res = new List<(string, string, string)>();
    foreach (var d in PickGroup(e).Where(d => !IsCms(d.Id) && !IsPlatform(d.Id)))
    {
        var v = await Resolve(d.Id, d.Range);
        if (v is not null && await HasUmbracoPath(d.Id, v)) res.Add((d.Id, v, d.Range));
    }
    return res;
}

async Task<bool> AllowsTarget(string id, RegEntry e, int hops = 3)
{
    var deps = PickGroup(e);
    var cms = deps.Where(d => IsCms(d.Id)).ToList();
    if (cms.Count > 0) return cms.All(d => Range(d.Range).Satisfies(target));
    if (hops <= 1) return false;
    // No direct Umbraco.Cms* dep: follow Umbraco deps (e.g. a meta-package over its Core).
    foreach (var d in deps.Where(d => !IsPlatform(d.Id)))
    {
        var v = await Resolve(d.Id, d.Range);
        if (v is null) continue;
        var de = (await Registration(d.Id)).First(x => x.Version.Equals(NuGetVersion.Parse(v)));
        if (await AllowsTarget(d.Id, de, hops - 1)) return true;
    }
    return false;
}

async Task<string?> LatestAllowing(string id)
{
    foreach (var e in (await Registration(id)).Where(e => e.Listed && !e.Version.IsPrerelease).OrderByDescending(e => e.Version))
        if (await AllowsTarget(id, e)) return e.Version.ToNormalizedString();
    return null;
}

static bool IsUnsafe(string name) =>
    name.StartsWith('/') || name.StartsWith('\\') || (name.Length > 1 && name[1] == ':') || name.Split('/', '\\').Contains("..");

async Task<Scan> ScanPackage(string id, string version)
{
    var key = $"{id}/{version}";
    if (scanCache.TryGetValue(key, out var cached)) return cached;
    var lid = id.ToLowerInvariant(); var lv = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
    var path = Path.Combine(tempDir, $"{lid}.{lv}.nupkg");
    if (!File.Exists(path))
    {
        using var resp = await http.GetAsync($"https://api.nuget.org/v3-flatcontainer/{lid}/{lv}/{lid}.{lv}.nupkg", HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > NupkgCap) throw new InvalidDataException($"{key}: over nupkg cap");
        await using var fs = File.Create(path);
        await resp.Content.CopyToAsync(fs);
    }
    if (new FileInfo(path).Length > NupkgCap) throw new InvalidDataException($"{key}: over nupkg cap");

    var scan = new Scan(id, version);
    long total = 0;
    using var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
    if (zip.Entries.Count > EntryCountCap) throw new InvalidDataException($"{key}: unsafe-archive (entry count)");
    var names = zip.Entries.Select(e => e.FullName).ToList();
    if (names.Any(IsUnsafe)) throw new InvalidDataException($"{key}: unsafe-archive (path)");

    scan.UmbracoPackageJson.AddRange(names.Where(n => n.Split('/').Last().Equals("umbraco-package.json", StringComparison.OrdinalIgnoreCase)));
    scan.PackageManifest.AddRange(names.Where(n => n.Split('/').Last().Equals("package.manifest", StringComparison.OrdinalIgnoreCase)));
    scan.Js.AddRange(names.Where(n => n.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)));

    foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
    {
        // Central-directory sizes are not trusted: count bytes actually read.
        var ms = new MemoryStream();
        using (var s = entry.Open())
        {
            var buf = new byte[81920]; int n; long entryBytes = 0;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                entryBytes += n; total += n;
                if (entryBytes > PerEntryCap || total > TotalCap) throw new InvalidDataException($"{key}: unsafe-archive (cap)");
                ms.Write(buf, 0, n);
            }
        }
        ms.Position = 0;
        dllCount++;
        scan.Dlls.Add(entry.FullName);
        scannedAssemblyNames.Add(Path.GetFileNameWithoutExtension(entry.Name));
        using var pe = new PEReader(ms);
        if (!pe.HasMetadata) continue;
        var md = pe.GetMetadataReader();
        if (md.IsAssembly) scannedAssemblyNames.Add(md.GetString(md.GetAssemblyDefinition().Name));
        foreach (var h in md.TypeReferences)
        {
            var (ns, name) = FullName(md, h);
            var full = $"{ns}.{name}";
            if (ns == H09Namespace)
            {
                scan.ManifestNsRefs.Add(full);
                if (h09Names.Contains(name)) scan.H09Refs.Add(full);
            }
            else if (h09Names.Contains(name) && ns.StartsWith("Umbraco", StringComparison.Ordinal)) scan.OtherManifestRefs.Add(full);
            if (ns.StartsWith("Umbraco.Cms.Api.Management", StringComparison.Ordinal)) scan.MgmtRefs.Add(full);
        }
        // A type that implements IPackageManifestReader supplies backoffice manifests from code instead of an
        // umbraco-package.json file; recorded so a "not working" row can be checked against what H09 really sees.
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            foreach (var ih in td.GetInterfaceImplementations())
            {
                var itf = md.GetInterfaceImplementation(ih).Interface;
                if (itf.Kind != HandleKind.TypeReference) continue;
                var (ins, iname) = FullName(md, (TypeReferenceHandle)itf);
                if (ins == H09Namespace && iname == "IPackageManifestReader")
                    scan.Implementers.Add($"{md.GetString(td.Namespace)}.{md.GetString(td.Name)}");
            }
        }
    }
    scan.H09Refs = scan.H09Refs.Distinct().ToList();
    scan.ManifestNsRefs = scan.ManifestNsRefs.Distinct().ToList();
    scan.MgmtRefs = scan.MgmtRefs.Distinct().ToList();
    scan.OtherManifestRefs = scan.OtherManifestRefs.Distinct().ToList();
    return scanCache[key] = scan;
}

// Nested type references carry no namespace; take it from the enclosing type reference.
static (string Ns, string Name) FullName(MetadataReader md, TypeReferenceHandle h)
{
    var tr = md.GetTypeReference(h);
    var name = md.GetString(tr.Name);
    if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
    {
        var (ons, oname) = FullName(md, (TypeReferenceHandle)tr.ResolutionScope);
        return (ons, $"{oname}+{name}");
    }
    return (md.GetString(tr.Namespace), name);
}

record RegEntry(NuGetVersion Version, bool Listed, List<(string Tfm, List<(string Id, string Range)> Deps)> Groups);

class Scan(string id, string version)
{
    public string Id { get; } = id;
    public string Version { get; } = version;
    public List<string> Dlls { get; } = [];
    public List<string> UmbracoPackageJson { get; } = [];
    public List<string> PackageManifest { get; } = [];
    public List<string> Js { get; } = [];
    public List<string> H09Refs { get; set; } = [];
    public List<string> ManifestNsRefs { get; set; } = [];
    public List<string> MgmtRefs { get; set; } = [];
    public List<string> OtherManifestRefs { get; set; } = [];
    public List<string> Implementers { get; } = [];
}

record Row(string Id, string Version, bool Working, bool Scanned, string Why, bool H09Own, bool H09Dep, int Mgmt, string? Swap, string? Depth2)
{
    public List<string> Deps { get; init; } = [];
    public List<string> H09Detail { get; init; } = [];
    public List<string> OtherManifestRefs { get; init; } = [];
    public List<string> Implementers { get; init; } = [];
}
