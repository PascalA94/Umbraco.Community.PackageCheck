using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PackageCheck.Core;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Sources.Cache;
using PackageCheck.Sources.Data;

namespace PackageCheck.Sources.Overrides;

/// <summary>
/// The community overrides release (design 7.8), compiled from this repo's <c>overrides/</c> folder by the overrides
/// workflow on every merge to main: <c>overrides.json</c> plus its <c>.sha256</c>, cached for 24 h. The hash comes from
/// the same release, so it catches a truncated or mismatched download, not a forged release; it is not a signature
/// (security review M1). Overrides are always labelled and keep the computed verdict beside them, and the majors table is
/// only taken when it looks sane. Any failure (network, HTTP status, hash mismatch, bad content) falls back
/// to the cached release, then to the snapshot bundled in the tool, with one warning. It never fails the run and never
/// makes it incomplete (design 7.3). The same asset carries the Umbraco majors table, which replaces the bundled one
/// when its <c>checked</c> date is newer. One fetch serves both.
/// </summary>
public sealed class CommunityOverrideSource : IOverrideSource, IUmbracoVersionsSource
{
    /// <summary>
    /// A fixed <c>overrides</c> release in this repo, updated in place (Pascal, 2026-11-06: no separate data repo). Not
    /// <c>latest</c>, which is the newest tool release. The download path needs no REST API, so CI runners that share an
    /// IP do not hit the API's unauthenticated rate limit.
    /// </summary>
    public static readonly Uri DefaultReleaseBase = new("https://github.com/PascalA94/Umbraco.Community.PackageCheck/releases/download/overrides/");

    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly CacheKey Key = new("community-overrides", "", "overrides.json", null, "v1");
    private const string SnapshotLabel = "bundled overrides snapshot";
    private const string ReleaseLabel = "community overrides release";

    private readonly HttpClient _http;
    private readonly FileCache? _cache;
    private readonly Uri? _releaseBase;
    private readonly bool _offline;
    private readonly Action<string>? _onWarning;
    private readonly TimeProvider _clock;
    private readonly Lazy<Task<Loaded>> _loaded;

    /// <param name="releaseBase">Null uses only the bundled snapshot (no HTTP): the tests' composition default.</param>
    public CommunityOverrideSource(HttpClient http, FileCache? cache, Uri? releaseBase, bool offline, Action<string>? onWarning = null, TimeProvider? clock = null)
    {
        _http = http;
        _cache = cache;
        _releaseBase = releaseBase;
        _offline = offline;
        _onWarning = onWarning;
        _clock = clock ?? TimeProvider.System;
        // Not bound to one caller's token: the first caller starts the fetch, which has its own timeout.
        _loaded = new Lazy<Task<Loaded>>(() => LoadOnceAsync(CancellationToken.None));
    }

    public async Task<OverrideSet> LoadAsync(CancellationToken ct) => (await _loaded.Value.WaitAsync(ct)).Overrides;

    public async Task<UmbracoVersionsTable> GetTableAsync(CancellationToken ct) => (await _loaded.Value.WaitAsync(ct)).Table;

    private sealed record Loaded(OverrideSet Overrides, UmbracoVersionsTable Table);

    /// <summary>The cached release: the body exactly as verified, and its hash, re-checked on every read.</summary>
    public sealed record CachedRelease(string Body, string Sha256);

    private async Task<Loaded> LoadOnceAsync(CancellationToken ct)
    {
        if (_releaseBase is null) return Snapshot();

        if (_offline)
            return FromCache(maxAge: null) ?? Snapshot();

        if (FromCache(MaxAge) is { } fresh) return fresh;

        string problem;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var shaText = Encoding.UTF8.GetString(await GetCappedAsync(new Uri(_releaseBase, "overrides.json.sha256"), 1024, cts.Token));
            var bytes = await GetCappedAsync(new Uri(_releaseBase, "overrides.json"), OverrideFileParser.MaxBytes, cts.Token);
            var expected = shaText.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
            // The hash covers the bytes as published, before any decoding.
            var body = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expected)
            {
                problem = "hash mismatch between overrides.json and overrides.json.sha256";
            }
            else if (TryParse(body, ReleaseLabel, out var loaded))
            {
                _cache?.Write(Key, new CachedRelease(body, Hash(body)));
                return loaded;
            }
            else
            {
                problem = "the release is not a valid overrides file";
            }
        }
        catch (Exception e)
        {
            // Overrides are optional (design 7.3): whatever goes wrong here degrades to the fallback, never fails the run.
            problem = e is HttpRequestException { StatusCode: { } status } ? $"HTTP {(int)status}" : e.GetType().Name;
        }

        var fallback = FromCache(maxAge: null);
        _onWarning?.Invoke($"community overrides could not be updated ({problem}); using the {(fallback is null ? "snapshot bundled with the tool" : "cached copy")}");
        return fallback ?? Snapshot();
    }

    private async Task<byte[]> GetCappedAsync(Uri url, int maxBytes, CancellationToken ct)
    {
        using var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
        {
            total += read;
            if (total > maxBytes) throw new InvalidDataException("response too large");
        }
        return buffer[..total];
    }

    private Loaded? FromCache(TimeSpan? maxAge)
    {
        if (_cache is null) return null;
        if (!_cache.TryRead<CachedRelease>(Key, maxAge, out var cached, c => c.Body is not null && c.Sha256 is not null)) return null;
        // A hand-edited or truncated entry is a miss, never trusted.
        if (Hash(cached.Body) != cached.Sha256) return null;
        return TryParse(cached.Body, ReleaseLabel, out var loaded) ? loaded : null;
    }

    private bool TryParse(string body, string label, out Loaded loaded)
    {
        loaded = null!;
        try
        {
            var root = JsonNode.Parse(body) as JsonObject ?? throw new InvalidDataException("not a JSON object");
            if (root["overrides"] is not JsonArray) throw new InvalidDataException("no overrides array");
            var set = OverrideFileParser.Parse(body, label, OverrideOrigin.Community, new ForwardingList(_onWarning));
            loaded = new Loaded(set, NewerTable(root["umbracoVersions"], DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime)));
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or UsageException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The release's table when it parses, was checked no earlier than the bundled one and no later than tomorrow, still
    /// lists every bundled major, and has a supported LTS today; else the bundled one. A release dated far ahead would
    /// otherwise pin itself for good, and one without an LTS would make every <c>latest-lts</c> run fail (review M1).
    /// </summary>
    private static UmbracoVersionsTable NewerTable(JsonNode? release, DateOnly today)
    {
        var bundled = BundledTable();
        if (release is not JsonObject obj) return bundled.Table;
        try
        {
            var checkedOn = CheckedDate(obj);
            if (checkedOn is null || (bundled.Checked is { } b && checkedOn < b) || checkedOn > today.AddDays(1)) return bundled.Table;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(obj.ToJsonString()));
            var table = UmbracoVersionsTable.Parse(stream);
            if (bundled.Table.Majors.Any(m => table.Find(m.Major) is null)) return bundled.Table;
            if (!table.Majors.Any(m => m.Lts && m.Eol >= today)) return bundled.Table;
            return table;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or FormatException)
        {
            return bundled.Table;
        }
    }

    private static (UmbracoVersionsTable Table, DateOnly? Checked) BundledTable()
    {
        using var reader = new StreamReader(BundledData.Open(BundledData.UmbracoVersionsResource));
        var text = reader.ReadToEnd();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return (UmbracoVersionsTable.Parse(stream), CheckedDate(JsonNode.Parse(text) as JsonObject));
    }

    private static DateOnly? CheckedDate(JsonObject? obj) =>
        obj?["checked"] is JsonValue v && v.TryGetValue<string>(out var s) && DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : null;

    private Loaded Snapshot()
    {
        using var reader = new StreamReader(BundledData.Open(BundledData.OverridesSeedResource));
        var set = OverrideFileParser.Parse(reader.ReadToEnd(), SnapshotLabel, OverrideOrigin.Community);
        return new Loaded(set, BundledTable().Table);
    }

    private static string Hash(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    /// <summary>Forwards the parser's per-entry warnings (an invalid entry is skipped, the rest still apply).</summary>
    private sealed class ForwardingList(Action<string>? onWarning) : List<string>, ICollection<string>
    {
        void ICollection<string>.Add(string item)
        {
            base.Add(item);
            onWarning?.Invoke(item);
        }
    }
}
