using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace M0Golden;

/// <summary>
/// Record/replay seam for every metadata request (NuGet registration, flat-container nuspec, Marketplace).
/// Goldens run on a pinned snapshot, never on live feeds (ruling R20), so the gate output is reproducible.
/// Replay mode has no inner network handler at all: a request the snapshot lacks is an error, not a live call.
/// </summary>
sealed class SnapshotHandler : DelegatingHandler
{
    public const string UserAgent = "umbraco-package-check-m0/0.0 (+https://github.com/PascalA94/Umbraco.Community.PackageCheck)";
    readonly string dir;
    readonly bool record;
    public int Served { get; private set; }
    public int RecordedCount { get; private set; }

    public SnapshotHandler(string dir, bool record)
        : base(record ? new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate } : new NoNetworkHandler())
    {
        this.dir = dir;
        this.record = record;
        Directory.CreateDirectory(dir);
    }

    static string Key(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.AbsoluteUri;
        // Belt and braces for design 6.1 / global constraints: this endpoint is never called, recorded or replayed.
        if (url.Contains("validatePackage", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("validatePackage must never be called");
        var key = Key(url);
        var metaPath = Path.Combine(dir, key + ".json");
        var bodyPath = Path.Combine(dir, key + ".body.gz");

        if (!record)
        {
            if (!File.Exists(metaPath)) throw new SnapshotMissException(url);
            var m = JsonSerializer.Deserialize<SnapshotMeta>(File.ReadAllText(metaPath))!;
            var bytes = Gunzip(File.ReadAllBytes(bodyPath));
            Served++;
            var res = new HttpResponseMessage((HttpStatusCode)m.Status) { Content = new ByteArrayContent(bytes), RequestMessage = request };
            if (m.ContentType is not null) res.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(m.ContentType);
            return res;
        }

        if (!request.Headers.UserAgent.Any()) request.Headers.UserAgent.ParseAdd(UserAgent);
        using var live = await base.SendAsync(request, ct);
        var data = await live.Content.ReadAsByteArrayAsync(ct);
        var status = (int)live.StatusCode;
        // Only stable answers go in the snapshot; a transient 429/5xx is returned to the caller (NuGet retries) but not pinned.
        if (status is 200 or 404)
        {
            if (status == 200 && MarketplaceSnapshot.IsMarketplace(url)) data = MarketplaceSnapshot.Strip(data);
            var meta = new SnapshotMeta(url, status, live.Content.Headers.ContentType?.ToString());
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllBytes(bodyPath, Gzip(data));
            RecordedCount++;
        }
        var copy = new HttpResponseMessage(live.StatusCode) { Content = new ByteArrayContent(data), RequestMessage = request };
        if (live.Content.Headers.ContentType is not null) copy.Content.Headers.ContentType = live.Content.Headers.ContentType;
        return copy;
    }

    static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) gz.Write(data);
        return ms.ToArray();
    }

    static byte[] Gunzip(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// Ruling R33: Marketplace bodies are committed with only the fields the engine reads. README content, descriptions and
/// other free text written by package authors are dropped before they reach the repo.
/// </summary>
static class MarketplaceSnapshot
{
    static readonly string[] Keep =
    [
        "id", "packageId", "title", "isRetired", "alternatePackageIdForRetiredPackage", "alternatePackageIdForRetiredPackageExistsInCatalog",
        "retiredReason", "isHQ", "latestVersionNumber", "latestVersionNumberForProvidedUmbracoVersion", "packageIdForProvidedUmbracoVersion",
        "versionSpecificPackageIds", "umbracoMajorVersionsSupported", "minimumUmbracoVersionNumber", "maximumUmbracoVersionNumber",
        "maximumUmbracoVersionNumberIsInclusive", "packageType", "packageSource",
    ];

    public static bool IsMarketplace(string url) => url.StartsWith("https://api.marketplace.umbraco.com/", StringComparison.OrdinalIgnoreCase);

    public static byte[] Strip(byte[] json)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject obj) return json;
        foreach (var name in obj.Select(p => p.Key).Where(k => !Keep.Contains(k)).ToList()) obj.Remove(name);
        return Encoding.UTF8.GetBytes(obj.ToJsonString());
    }
}

sealed record SnapshotMeta(string Url, int Status, string? ContentType);

sealed class SnapshotMissException(string url) : Exception($"not in the recorded snapshot: {url} (re-record with --record)");

/// <summary>Inner handler for replay mode: proves no metadata request leaves the machine.</summary>
sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        throw new InvalidOperationException($"network call in replay mode: {request.RequestUri}");
}
