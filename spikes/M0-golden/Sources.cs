using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace M0Golden;

sealed record DeprecationInfo(IReadOnlyList<string> Reasons, string? AlternateId, string? Message);

sealed record PkgVersion(string Id, NuGetVersion Version, bool Listed, IReadOnlyList<PackageDependencyGroup> Groups, DeprecationInfo? Deprecation, DateTimeOffset? Published);

sealed record MarketplaceInfo(bool IsRetired, string? AlternateId, string? RetiredReason, bool IsHq, string? LatestForTarget);

/// <summary>
/// NuGet metadata through NuGet.Protocol (spike S1 GO), with our handler in the HttpHandlerResource seam (ruling R22).
/// </summary>
sealed class Feed
{
    readonly PackageMetadataResource meta;
    readonly HttpClient http;
    // NoCache + a private HTTP cache path: NuGet's own disk cache sits below the seam and would hide requests (S1 finding).
    readonly SourceCacheContext cache = new() { NoCache = true, DirectDownload = true };
    readonly Dictionary<string, IReadOnlyList<PkgVersion>> versions = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, MarketplaceInfo?> marketplace = new(StringComparer.OrdinalIgnoreCase);

    Feed(PackageMetadataResource meta, HttpClient http) { this.meta = meta; this.http = http; }

    public static async Task<Feed> CreateAsync(HttpMessageHandler seamHandler)
    {
        var source = new NuGet.Configuration.PackageSource("https://api.nuget.org/v3/index.json", "nuget.org");
        var providers = Repository.Provider.GetCoreV3().Where(p => p.Value.ResourceType != typeof(HttpHandlerResource)).ToList();
        providers.Add(new Lazy<INuGetResourceProvider>(() => new SeamProvider(seamHandler)));
        var repo = new SourceRepository(source, providers);
        var meta = await repo.GetResourceAsync<PackageMetadataResource>() ?? throw new InvalidOperationException("no PackageMetadataResource");
        var http = new HttpClient(seamHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
        return new Feed(meta, http);
    }

    public async Task<IReadOnlyList<PkgVersion>> VersionsAsync(string id)
    {
        if (versions.TryGetValue(id, out var hit)) return hit;
        // includeUnlisted: a site can pin an unlisted version, and its metadata still decides its verdict; candidates filter on Listed.
        var all = await meta.GetMetadataAsync(id, includePrerelease: true, includeUnlisted: true, cache, NuGet.Common.NullLogger.Instance, CancellationToken.None);
        var list = new List<PkgVersion>();
        foreach (var m in all)
        {
            var d = await m.GetDeprecationMetadataAsync();
            var dep = d is null ? null : new DeprecationInfo(d.Reasons?.ToList() ?? [], d.AlternatePackage?.PackageId, d.Message);
            list.Add(new PkgVersion(m.Identity.Id, m.Identity.Version, m.IsListed, m.DependencySets.ToList(), dep, m.Published));
        }
        list.Sort((a, b) => a.Version.CompareTo(b.Version));
        return versions[id] = list;
    }

    /// <summary>The target's .NET TFM from the Umbraco.Cms nuspec for that version (design 6.1, target resolution).</summary>
    public async Task<NuGetFramework> TargetTfmAsync(NuGetVersion cms)
    {
        var v = cms.ToNormalizedString().ToLowerInvariant();
        var xml = await http.GetStringAsync($"https://api.nuget.org/v3-flatcontainer/umbraco.cms/{v}/umbraco.cms.nuspec");
        var doc = SafeXml.Parse(xml);
        var tfms = doc.Descendants().Where(e => e.Name.LocalName == "group").Select(e => (string?)e.Attribute("targetFramework"))
            .Where(t => !string.IsNullOrEmpty(t)).Select(t => NuGetFramework.Parse(t!)).ToList();
        if (tfms.Count == 0) throw new InvalidOperationException($"Umbraco.Cms {cms} nuspec has no dependency groups");
        return tfms.OrderByDescending(t => t.Version).First();
    }

    /// <summary>Marketplace detail (read-only GET; validatePackage is never called). Null on 404 or any error: Marketplace is never a hard failure.</summary>
    public async Task<MarketplaceInfo?> MarketplaceAsync(string id, NuGetVersion target)
    {
        var key = $"{id}@{target}";
        if (marketplace.TryGetValue(key, out var hit)) return hit;
        MarketplaceInfo? info = null;
        try
        {
            using var res = await http.GetAsync($"https://api.marketplace.umbraco.com/api/v1.0/packages/{Uri.EscapeDataString(id)}?SupportsUmbracoVersionNumber={Uri.EscapeDataString(target.ToNormalizedString())}");
            if (res.IsSuccessStatusCode)
            {
                var j = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
                info = new MarketplaceInfo(
                    j["isRetired"]?.GetValue<bool>() ?? false,
                    j["alternatePackageIdForRetiredPackage"]?.GetValue<string>(),
                    j["retiredReason"]?.GetValue<string>(),
                    j["isHQ"]?.GetValue<bool>() ?? false,
                    j["latestVersionNumberForProvidedUmbracoVersion"]?.GetValue<string>());
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { info = null; }
        return marketplace[key] = info;
    }
}

sealed class SeamProvider(HttpMessageHandler inner) : ResourceProvider(typeof(HttpHandlerResource), nameof(SeamProvider), NuGetResourceProviderPositions.Last)
{
    public override Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token) =>
        Task.FromResult(new Tuple<bool, INuGetResource?>(true, new HttpHandlerResourceV3(new HttpClientHandler(), inner)));
}

static class SafeXml
{
    // Ruling R21: package-controlled XML is parsed with DTDs prohibited, no resolver and a size cap.
    public static XDocument Parse(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10_000_000 };
        using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿')), settings);
        return XDocument.Load(reader);
    }
}

/// <summary>
/// Nupkgs are downloaded once into a cache outside the repo (licences vary, design 9) and pinned by sha512 in
/// recorded/nupkgs.json, so a replay run opens byte-identical packages or fails.
/// </summary>
sealed class NupkgStore
{
    const long NupkgCap = 250L * 1024 * 1024;
    readonly HttpClient http;
    readonly string cacheDir;
    readonly string pinsPath;
    readonly bool record;
    readonly SortedDictionary<string, NupkgPin> pins = new(StringComparer.OrdinalIgnoreCase);
    public SortedDictionary<string, NupkgPin> Used { get; } = new(StringComparer.OrdinalIgnoreCase);

    public NupkgStore(string cacheDir, string pinsPath, bool record)
    {
        this.cacheDir = cacheDir;
        this.pinsPath = pinsPath;
        this.record = record;
        Directory.CreateDirectory(cacheDir);
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(SnapshotHandler.UserAgent);
        if (!record && File.Exists(pinsPath))
            foreach (var p in JsonSerializer.Deserialize<List<NupkgPin>>(File.ReadAllText(pinsPath))!) pins[Key(p.Id, p.Version)] = p;
    }

    static string Key(string id, string version) => $"{id.ToLowerInvariant()}/{version.ToLowerInvariant()}";

    /// <summary>Returns the local path, or null when the nupkg is over the size cap (metadata-only fallback, design 4).</summary>
    public async Task<string?> GetAsync(string id, NuGetVersion version)
    {
        var lid = id.ToLowerInvariant();
        var lv = version.ToNormalizedString().ToLowerInvariant();
        var key = Key(id, lv);
        var path = Path.Combine(cacheDir, $"{lid}.{lv}.nupkg");
        pins.TryGetValue(key, out var pin);
        if (!record && pin is null) throw new SnapshotMissException($"nupkg {id} {lv} has no sha512 pin in recorded/nupkgs.json");

        if (!File.Exists(path) || (pin is not null && Sha512(path) != pin.Sha512))
        {
            using var res = await http.GetAsync($"https://api.nuget.org/v3-flatcontainer/{lid}/{lv}/{lid}.{lv}.nupkg", HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            if (res.Content.Headers.ContentLength > NupkgCap) return null;
            var tmp = path + ".tmp";
            // R34: Content-Length can be absent or wrong, so the cap is enforced on bytes actually received.
            var over = false;
            await using (var fs = File.Create(tmp))
            await using (var body = await res.Content.ReadAsStreamAsync())
            {
                var buf = new byte[81920]; int n; long got = 0;
                while ((n = await body.ReadAsync(buf)) > 0)
                {
                    got += n;
                    if (got > NupkgCap) { over = true; break; }
                    await fs.WriteAsync(buf.AsMemory(0, n));
                }
            }
            if (over) { File.Delete(tmp); return null; }
            File.Move(tmp, path, overwrite: true);
        }
        if (new FileInfo(path).Length > NupkgCap) return null;
        var sha = Sha512(path);
        if (pin is not null && pin.Sha512 != sha) throw new InvalidDataException($"{id} {lv}: sha512 does not match the recorded pin");
        Used[key] = pins[key] = new NupkgPin(id, version.ToNormalizedString(), sha);
        return path;
    }

    public void SavePins() =>
        File.WriteAllText(pinsPath, JsonSerializer.Serialize(Used.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }) + "\n");

    static string Sha512(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToBase64String(SHA512.HashData(s));
    }
}

sealed record NupkgPin(string Id, string Version, string Sha512);
