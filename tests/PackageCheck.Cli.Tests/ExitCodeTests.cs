using System.Net;
using System.Text.Json;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

public class ExitCodeTests
{
    private const string NuGet = "https://api.nuget.org/";
    private const string Marketplace = "https://api.marketplace.umbraco.com/";

    // A 1 ms Retry-After keeps the policy's three tries fast (a zero or missing value falls back to the backoff).
    private static Task<HttpResponseMessage> Down(HttpRequestMessage _, CancellationToken __)
    {
        var r = FixtureHttpHandler.Json(HttpStatusCode.ServiceUnavailable, null);
        r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
        return Task.FromResult(r);
    }

    private static JsonElement Json(CliResult r) => JsonDocument.Parse(r.Stdout).RootElement.Clone();

    private static string[] Scan(string site, params string[] extra) =>
        ["scan", site, "--no-inspect", "--no-cache", "--format", "json", .. extra];

    [Fact]
    public async Task FailOnSuspicious_OneSuspicious_Exit1()
    {
        // Astroboard 1.1.3 at 17.7.2 without inspection is suspicious (ruling R26); the bundled override lifts it to no-release.
        var r = await CliHarness.RunAsync(TestSites.Handler(), Scan(TestSites.Create("17.7.2", ("Astroboard", "1.1.3")), "--target", "17.7.2", "--fail-on", "suspicious"));

        Assert.Equal(1, r.Code);
        Assert.False(Json(r).GetProperty("summary").GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public async Task FailOnNone_Findings_Exit0()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), Scan(TestSites.Create("17.7.2", ("Astroboard", "1.1.3")), "--target", "17.7.2"));

        Assert.Equal(0, r.Code);
    }

    [Fact]
    public async Task FailOnSuspicious_NuGetUnreachable_Exit3()
    {
        var handler = TestSites.Handler().Route(NuGet, Down);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--target", "17.7.2", "--fail-on", "suspicious"));

        Assert.Equal(3, r.Code);
    }

    [Fact]
    public async Task FailOnNone_NuGetUnreachable_Exit0_Incomplete()
    {
        var handler = TestSites.Handler().Route(NuGet, Down);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--target", "17.7.2"));

        Assert.Equal(0, r.Code);
        Assert.True(Json(r).GetProperty("summary").GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public async Task Both1And3_Returns3()
    {
        var handler = TestSites.Handler().Route(NuGet + "v3/registration5-gz-semver2/pcdown.", Down);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Astroboard", "1.1.3"), ("PcDown.Pkg", "1.0.0")), "--target", "17.7.2", "--fail-on", "suspicious"));

        Assert.Equal(3, r.Code);
    }

    [Fact]
    public async Task Transitive_NoLock_WithFailOn_Exit3()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), Scan(TestSites.Create("17.7.2"), "--target", "17.7.2", "--transitive", "--fail-on", "suspicious"));

        Assert.Equal(3, r.Code);
        Assert.Equal("unavailable", Json(r).GetProperty("summary").GetProperty("transitive").GetString());
    }

    [Fact]
    public async Task NotFoundAndPrivateFeedOnly_NotIncomplete_Exit0()
    {
        var site = TestSites.Create("17.7.2", ("PcMissing.Pkg", "1.0.0"), ("Our.Private", "1.0.0"));
        var ids = Path.Combine(site, "private.txt");
        File.WriteAllText(ids, "Our.Private\n");
        var handler = TestSites.Handler()
            .Route(NuGet + "v3/registration5-gz-semver2/pcmissing.", HttpStatusCode.NotFound)
            .Route(Marketplace, HttpStatusCode.NotFound);

        var r = await CliHarness.RunAsync(handler, Scan(site, "--target", "17.7.2", "--fail-on", "suspicious", "--private-ids", ids));

        Assert.Equal(0, r.Code);
        var root = Json(r);
        Assert.False(root.GetProperty("summary").GetProperty("incomplete").GetBoolean());
        Assert.Equal(["not-found", "private-feed"],
            root.GetProperty("sites")[0].GetProperty("packages").EnumerateArray().Select(p => p.GetProperty("reason").GetString()).Order());
    }

    [Fact]
    public async Task MarketplaceDown_NeverExit3()
    {
        var handler = TestSites.Handler().Route(Marketplace, Down);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--target", "17.7.2", "--fail-on", "suspicious"));

        Assert.InRange(r.Code, 0, 1);
        Assert.False(Json(r).GetProperty("summary").GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public async Task LocalOverridesUrlDown_NeverExit3()
    {
        var handler = TestSites.Handler().Route("https://overrides.example.test/", Down);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")),
            "--target", "17.7.2", "--fail-on", "suspicious", "--overrides", "https://overrides.example.test/overrides.yml"));

        Assert.InRange(r.Code, 0, 1);
        var root = Json(r);
        Assert.False(root.GetProperty("summary").GetProperty("incomplete").GetBoolean());
        Assert.NotEqual(0, root.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task TargetUnresolvable_SourceDown_FailOn_Exit3()
    {
        var handler = TestSites.Handler().Route(NuGet, Down);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--target", "17", "--fail-on", "suspicious"));

        Assert.Equal(3, r.Code);
        var site = Json(r).GetProperty("sites")[0];
        Assert.Equal("17", site.GetProperty("target").GetProperty("resolved").GetString());
        Assert.Contains(site.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "target could not be resolved: source error");
        var pkg = site.GetProperty("packages")[0];
        Assert.Equal("unknown", pkg.GetProperty("verdict").GetString());
        Assert.Equal("source-error", pkg.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task TargetUnresolvable_SourceDown_NoFailOn_Exit0_Incomplete()
    {
        var handler = TestSites.Handler().Route(NuGet, Down);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--target", "17"));

        Assert.Equal(0, r.Code);
        Assert.True(Json(r).GetProperty("summary").GetProperty("incomplete").GetBoolean());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "private feed")]
    [InlineData(HttpStatusCode.NotFound, "not found")]
    public async Task TargetUnresolvable_FeedRefuses_FailOn_Exit3(HttpStatusCode status, string warning)
    {
        // Review M4 F1: a target nothing could be judged against is never a green run with --fail-on set.
        var handler = TestSites.Handler().Route(NuGet + "v3/registration5-gz-semver2/umbraco.cms/", status);

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--target", "17", "--fail-on", "suspicious"));

        Assert.Equal(3, r.Code);
        var site = Json(r).GetProperty("sites")[0];
        Assert.Contains(site.GetProperty("warnings").EnumerateArray(), w => w.GetString() == $"target could not be resolved: {warning}");
    }

    [Fact]
    public async Task RemoteOverrides_Malformed_IsAWarning()
    {
        // Security review M4-S9 / design 7.3: a bad overrides URL degrades the run, never aborts it.
        var handler = TestSites.Handler().Route("https://overrides.example.test/", HttpStatusCode.OK, "garbage: [");

        var r = await CliHarness.RunAsync(handler, Scan(TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")),
            "--target", "17.7.2", "--overrides", "https://overrides.example.test/overrides.yml"));

        Assert.Equal(0, r.Code);
        Assert.Contains(Json(r).GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("overrides", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TargetDoesNotExist_Exit2()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), Scan(TestSites.Create("17.7.2"), "--target", "99", "--fail-on", "suspicious"));

        Assert.Equal(2, r.Code);
        Assert.Contains("no 99.x release", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_ColdCache_MajorTarget_UnknownOffline_NotIncomplete()
    {
        // Preflight 1.34: an offline miss is "unknown, offline", an expected gap, not a failed source.
        var r = await CliHarness.RunAsync(TestSites.Handler(),
            ["scan", TestSites.Create("17.7.2", ("Umbraco.Community.Contentment", "6.2.1")), "--offline", "--format", "json", "--target", "17", "--fail-on", "suspicious"]);

        Assert.Equal(0, r.Code);
        var site = Json(r).GetProperty("sites")[0];
        Assert.Equal("offline", site.GetProperty("packages")[0].GetProperty("reason").GetString());
        Assert.Contains(site.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "target could not be resolved: offline");
    }
}
