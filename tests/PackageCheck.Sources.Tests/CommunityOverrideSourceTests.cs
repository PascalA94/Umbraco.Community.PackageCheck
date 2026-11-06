using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using PackageCheck.Core.Overrides;
using PackageCheck.Sources.Cache;
using PackageCheck.Sources.Overrides;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

public sealed class CommunityOverrideSourceTests : IDisposable
{
    private static readonly Uri Base = new("https://example.test/compat/releases/latest/download/");
    private readonly string _root = Directory.CreateTempSubdirectory("pc-community-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly FixtureHttpHandler _handler = new(Path.Combine(Path.GetTempPath(), "pc-no-fixtures"), RecordMode.Replay);
    private readonly List<string> _warnings = [];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string VersionsWith19 = """
        { "checked": "2026-12-01", "majors": [
          { "major": 13, "lts": true, "eol": "2026-12-14", "tfm": "net8.0" },
          { "major": 14, "lts": false, "eol": "2025-05-30", "tfm": "net8.0" },
          { "major": 15, "lts": false, "eol": "2025-11-14", "tfm": "net9.0" },
          { "major": 16, "lts": false, "eol": "2026-06-12", "tfm": "net9.0" },
          { "major": 17, "lts": true, "eol": "2028-11-27", "tfm": "net10.0" },
          { "major": 18, "lts": false, "eol": "2027-06-25", "tfm": "net10.0" },
          { "major": 19, "lts": false, "eol": "2028-12-01", "tfm": "net11.0" } ] }
        """;

    private static string Asset(string id = "Community.Only", string status = "partial", string? versions = null) => $$"""
        { "schemaVersion": 1,
          "overrides": [ { "id": "{{id}}", "entries": [ {
            "packageVersions": "[1.0.0, )", "umbraco": "[17.0.0, )", "status": "{{status}}",
            "summary": "Community entry for tests", "evidence": "https://example.test/issues/1",
            "reportedBy": "someone", "verifiedOn": "2026-12-01", "reviewBy": "2027-12-01", "replacement": null } ] } ],
          "umbracoVersions": {{versions ?? """{ "checked": "2026-01-01", "majors": [ { "major": 17, "lts": true, "eol": "2028-11-27", "tfm": "net10.0" } ] }"""}} }
        """;

    private static string Sha(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant() + "  overrides.json\n";

    private void Serve(string body, string? sha = null)
    {
        _handler.Route(Base + "overrides.json.sha256", (_, _) => Task.FromResult(Text(HttpStatusCode.OK, sha ?? Sha(body))));
        _handler.Route(Base + "overrides.json", (_, _) => Task.FromResult(Text(HttpStatusCode.OK, body)));
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private CommunityOverrideSource Source(FileCache? cache = null, bool offline = false, Uri? releaseBase = null) =>
        new(new HttpClient(_handler), cache, releaseBase ?? Base, offline, _warnings.Add, _clock);

    private static async Task<IReadOnlyList<OverrideEntry>> Snapshot() => (await new BundledOverrideSource().LoadAsync(Ct)).Entries;

    private static IEnumerable<string> Ids(IEnumerable<OverrideEntry> entries) => entries.Select(e => e.Id).Order(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task VerifiedAsset_IsUsed_AndCached()
    {
        Serve(Asset());
        var cache = new FileCache(_root, _clock);

        var set = await Source(cache).LoadAsync(Ct);

        var entry = Assert.Single(set.Entries);
        Assert.Equal(("Community.Only", OverrideOrigin.Community), (entry.Id, entry.Origin));
        Assert.Empty(_warnings);

        // Within 24 h a second run is served from the cache with no request.
        var before = _handler.RequestCount;
        Assert.Single((await Source(cache).LoadAsync(Ct)).Entries);
        Assert.Equal(before, _handler.RequestCount);
    }

    [Fact]
    public async Task HashMismatch_FallsBackToSnapshot_WithWarning()
    {
        Serve(Asset(), sha: new string('0', 64) + "  overrides.json\n");

        var set = await Source().LoadAsync(Ct);

        Assert.Equal(Ids(await Snapshot()), Ids(set.Entries));
        Assert.Contains("hash mismatch", Assert.Single(_warnings));
    }

    [Fact]
    public async Task NetworkError_FallsBackToCache()
    {
        var cache = new FileCache(_root, _clock);
        Serve(Asset("Cached.Package"));
        await Source(cache).LoadAsync(Ct);
        _clock.Advance(TimeSpan.FromHours(25));
        var failing = new FixtureHttpHandler(Path.Combine(Path.GetTempPath(), "pc-no-fixtures"), RecordMode.Replay)
            .Route(Base.ToString(), HttpStatusCode.InternalServerError);

        var set = await new CommunityOverrideSource(new HttpClient(failing), cache, Base, offline: false, _warnings.Add).LoadAsync(Ct);

        Assert.Equal("Cached.Package", Assert.Single(set.Entries).Id);
        Assert.True(failing.RequestCount > 0);
        Assert.Single(_warnings);
    }

    [Fact]
    public async Task Offline_UsesSnapshotOnly_NoHttp()
    {
        Serve(Asset());

        var set = await Source(offline: true).LoadAsync(Ct);

        Assert.Equal(0, _handler.RequestCount);
        Assert.Equal(Ids(await Snapshot()), Ids(set.Entries));
        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task NoReleaseConfigured_UsesSnapshot_NoHttp()
    {
        var set = await new CommunityOverrideSource(new HttpClient(_handler), null, null, offline: false, _warnings.Add).LoadAsync(Ct);

        Assert.Equal(0, _handler.RequestCount);
        Assert.Equal(Ids(await Snapshot()), Ids(set.Entries));
    }

    [Fact]
    public async Task MalformedAsset_FallsBackToSnapshot_WithWarning()
    {
        Serve("{ not json");

        var set = await Source().LoadAsync(Ct);

        Assert.Equal(Ids(await Snapshot()), Ids(set.Entries));
        Assert.Single(_warnings);
    }

    [Fact]
    public async Task OversizedAsset_FallsBackToSnapshot_WithWarning()
    {
        Serve(new string(' ', OverrideFileParser.MaxBytes + 10));

        var set = await Source().LoadAsync(Ct);

        Assert.Equal(Ids(await Snapshot()), Ids(set.Entries));
        Assert.Single(_warnings);
    }

    [Fact]
    public async Task LocalOverrides_StillWin()
    {
        Serve(Asset("My.Package", status: "partial"));
        var localFile = Path.Combine(_root, "local.yml");
        await File.WriteAllTextAsync(localFile, """
            id: My.Package
            entries:
              - packageVersions: "[1.0.0, )"
                umbraco: "[17.0.0, )"
                status: works
                summary: "Works on our sites"
                evidence: https://example.test/issues/2
                verifiedOn: "2026-12-01"
            """, Ct);
        var composite = new CompositeOverrideSource([Source(), new LocalOverrideSource(localFile, new HttpClient(_handler))]);

        var entry = Assert.Single((await composite.LoadAsync(Ct)).Entries, e => e.Id == "My.Package");

        Assert.Equal((OverrideOrigin.Local, OverrideStatus.Works), (entry.Origin, entry.Status));
    }

    [Fact]
    public async Task NewerVersionsTable_ReplacesEmbedded()
    {
        Serve(Asset(versions: VersionsWith19));

        var table = await Source().GetTableAsync(Ct);

        Assert.Contains(table.Majors, m => m.Major == 19);
    }

    [Theory]
    // Dated far ahead (would pin itself forever), no supported LTS (latest-lts would exit 2), a bundled major dropped.
    [InlineData("""{ "checked": "9999-12-31", "majors": [ { "major": 13, "lts": true, "eol": "2026-12-14", "tfm": "net8.0" }, { "major": 14, "lts": false, "eol": "2025-05-30", "tfm": "net8.0" }, { "major": 15, "lts": false, "eol": "2025-11-14", "tfm": "net9.0" }, { "major": 16, "lts": false, "eol": "2026-06-12", "tfm": "net9.0" }, { "major": 17, "lts": true, "eol": "2028-11-27", "tfm": "net10.0" }, { "major": 18, "lts": false, "eol": "2027-06-25", "tfm": "net10.0" }, { "major": 19, "lts": false, "eol": "2028-12-01", "tfm": "net11.0" } ] }""")]
    [InlineData("""{ "checked": "2026-12-01", "majors": [ { "major": 13, "lts": false, "eol": "2026-12-14", "tfm": "net8.0" }, { "major": 14, "lts": false, "eol": "2025-05-30", "tfm": "net8.0" }, { "major": 15, "lts": false, "eol": "2025-11-14", "tfm": "net9.0" }, { "major": 16, "lts": false, "eol": "2026-06-12", "tfm": "net9.0" }, { "major": 17, "lts": false, "eol": "2028-11-27", "tfm": "net10.0" }, { "major": 18, "lts": false, "eol": "2027-06-25", "tfm": "net10.0" }, { "major": 19, "lts": false, "eol": "2028-12-01", "tfm": "net11.0" } ] }""")]
    [InlineData("""{ "checked": "2026-12-01", "majors": [ { "major": 17, "lts": true, "eol": "2028-11-27", "tfm": "net10.0" }, { "major": 19, "lts": false, "eol": "2028-12-01", "tfm": "net11.0" } ] }""")]
    public async Task ImplausibleVersionsTable_KeepsEmbedded(string versions)
    {
        // Security review M1: the release is only hash-checked against itself, so its table must also look sane.
        Serve(Asset(versions: versions));

        var table = await Source().GetTableAsync(Ct);

        Assert.DoesNotContain(table.Majors, m => m.Major == 19);
        Assert.Contains(table.Majors, m => m.Major == 13);
    }

    [Fact]
    public async Task OlderVersionsTable_KeepsEmbedded()
    {
        Serve(Asset());

        var table = await Source().GetTableAsync(Ct);

        // The release table above was checked in January 2026 and lists only 17; the bundled one is newer.
        Assert.Contains(table.Majors, m => m.Major == 13);
    }

    [Fact]
    public async Task OverridesAndTable_ShareOneFetch()
    {
        Serve(Asset(versions: VersionsWith19));
        var source = Source();

        await source.GetTableAsync(Ct);
        await source.LoadAsync(Ct);

        Assert.Equal(2, _handler.RequestCount);
    }
}
