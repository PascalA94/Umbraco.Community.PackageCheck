using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

/// <summary>Task 32 through the real composition: the community release is fetched, applied, cached and optional.</summary>
public class CommunityOverridesTests
{
    private static readonly Uri Release = new("https://compat.example.test/releases/latest/download/");

    private const string Asset = """
        { "schemaVersion": 1,
          "overrides": [ { "id": "Umbraco.Community.Contentment", "entries": [ {
            "packageVersions": "[6.0.0, )", "umbraco": "[17.0.0, )", "status": "partial",
            "summary": "Community test entry", "evidence": "https://example.test/issues/1",
            "reportedBy": "someone", "verifiedOn": "2026-11-01", "reviewBy": "2027-11-01", "replacement": null } ] } ],
          "umbracoVersions": { "checked": "2026-01-01", "majors": [ { "major": 17, "lts": true, "eol": "2028-11-27", "tfm": "net10.0" } ] } }
        """;

    private static Task<HttpResponseMessage> Down(HttpRequestMessage _, CancellationToken __)
    {
        var r = FixtureHttpHandler.Json(HttpStatusCode.ServiceUnavailable, null);
        r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
        return Task.FromResult(r);
    }

    private static string[] Scan(string site, params string[] extra) =>
        ["scan", site, "--no-inspect", "--format", "json", "--target", "17.7.2", .. extra];

    private static JsonElement Package(CliResult r) =>
        JsonDocument.Parse(r.Stdout).RootElement.GetProperty("sites")[0].GetProperty("packages")[0].Clone();

    [Fact]
    public async Task CommunityFailure_NeverIncomplete()
    {
        var handler = TestSites.Handler().Route(Release.ToString(), Down);

        var r = await CliHarness.RunWithReleaseAsync(handler, Release, null,
            Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--no-cache", "--fail-on", "suspicious"));

        Assert.Equal(0, r.Code);
        var root = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.False(root.GetProperty("summary").GetProperty("incomplete").GetBoolean());
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("community overrides"));
        Assert.True(handler.CountFor(Release.ToString()) > 0);
    }

    [Fact]
    public async Task CommunityRelease_IsApplied_ThenServedFromCache()
    {
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Asset))).ToLowerInvariant();
        var handler = TestSites.Handler()
            .Route(Release + "overrides.json.sha256", HttpStatusCode.OK, sha + "  overrides.json\n")
            .Route(Release + "overrides.json", HttpStatusCode.OK, Asset);
        var cache = Directory.CreateTempSubdirectory("pc-cli-community-").FullName;
        var site = TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1"));

        var first = await CliHarness.RunWithReleaseAsync(handler, Release, cache, Scan(site));
        var pkg = Package(first);
        Assert.Equal("suspicious", pkg.GetProperty("verdict").GetString());
        Assert.Equal("ok", pkg.GetProperty("override").GetProperty("computedVerdict").GetString());
        Assert.Equal(2, handler.CountFor(Release.ToString()));

        var second = await CliHarness.RunWithReleaseAsync(handler, Release, cache, Scan(site, "--offline"));
        Assert.Equal("suspicious", Package(second).GetProperty("verdict").GetString());
        Assert.Equal(2, handler.CountFor(Release.ToString()));
    }
}
