using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PackageCheck.Core.Model;
using PackageCheck.Inputs;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

public class ScanCommandTests
{
    private const string Contentment = "Umbraco.Community.Contentment";

    private static JsonElement Json(CliResult r) => JsonDocument.Parse(r.Stdout).RootElement.Clone();

    [Fact]
    public async Task NoUmbracoProject_Exit2_Message()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.FromFixture("shape-10"), "--target", "17.7.2");

        Assert.Equal(2, r.Code);
        Assert.Contains("no Umbraco project found", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPath_Exit2()
    {
        var missing = Path.Combine(Path.GetTempPath(), "pc-does-not-exist-" + Guid.NewGuid().ToString("N"));

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", missing);

        Assert.Equal(2, r.Code);
        Assert.Contains($"path not found: {missing}", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiPath_OneWithoutUmbraco_WarnsAndSkips()
    {
        var site = TestSites.Create("17.7.2");
        var lib = TestSites.FromFixture("shape-10");

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", site, lib, "--target", "17.7.2", "--format", "json", "--no-inspect");

        Assert.Equal(0, r.Code);
        var root = Json(r);
        Assert.Equal(1, root.GetProperty("sites").GetArrayLength());
        Assert.Equal([$"no Umbraco project found in {lib}; skipped"], root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task MultiPath_NoneWithUmbraco_Exit2()
    {
        var empty = Directory.CreateTempSubdirectory("pc-cli-empty-").FullName;

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.FromFixture("shape-10"), empty, "--target", "17.7.2");

        Assert.Equal(2, r.Code);
        Assert.Contains("no Umbraco project found", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiPath_OneMissing_Exit2()
    {
        var handler = TestSites.Handler();

        var r = await CliHarness.RunAsync(handler, "scan", TestSites.Create("17.7.2"), "/does/not/exist", "--target", "17.7.2");

        Assert.Equal(2, r.Code);
        Assert.Contains("path not found: /does/not/exist", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task DuplicatePath_ScannedOnce()
    {
        var site = TestSites.Create("17.7.2");

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", site, site + Path.DirectorySeparatorChar, "--target", "17.7.2", "--format", "json", "--no-inspect");

        Assert.Equal(0, r.Code);
        Assert.Equal(1, Json(r).GetProperty("sites").GetArrayLength());
        Assert.Equal(0, Json(r).GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task NextMajorUnreleased_Exit2()
    {
        // The recording (2026-11-05) has 19.0.0-beta1 and no stable 19.x.
        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.Create("18.3.1"), "--target", "next-major", "--no-inspect");

        Assert.Equal(2, r.Code);
        Assert.Contains("no stable 19.x yet; pass --prerelease or an exact version", r.Stderr, StringComparison.Ordinal);
    }

    private sealed class ThrowingReader : IProjectReader
    {
        public Task<ScanInput> ReadAsync(string path, InputOptions o, CancellationToken ct) =>
            throw new InvalidOperationException("boom\nsecond line \u001b]0;title\u0007 https://user:s3cret@feed.example.test/x");
    }

    [Fact]
    public async Task UnhandledException_Exit2_OneLine()
    {
        void Throwing(IServiceCollection s) => s.AddSingleton<IProjectReader, ThrowingReader>();
        var site = TestSites.Create("17.7.2");

        var quiet = await CliHarness.RunWithAsync(TestSites.Handler(), null, Throwing, "scan", site, "--target", "17.7.2");
        var verbose = await CliHarness.RunWithAsync(TestSites.Handler(), null, Throwing, "scan", site, "--target", "17.7.2", "--verbose");

        Assert.Equal(2, quiet.Code);
        Assert.Single(quiet.Stderr.TrimEnd().Split('\n'));
        Assert.Contains("boom", quiet.Stderr, StringComparison.Ordinal);
        // Review M4 F9 / M4-S8: no terminal control characters and no userinfo on stderr.
        Assert.DoesNotContain('\u001b', quiet.Stderr);
        Assert.DoesNotContain('\u0007', quiet.Stderr);
        Assert.DoesNotContain("s3cret", quiet.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, verbose.Code);
        Assert.Contains("   at ", verbose.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivateIds_SkippedWithoutHttp()
    {
        var site = TestSites.Create("17.7.2", (Contentment, "6.2.1"));
        var ids = Path.Combine(site, "private-ids.txt");
        File.WriteAllText(ids, "# our own feed\numbraco.community.CONTENTMENT\n");
        var handler = TestSites.Handler();

        var r = await CliHarness.RunAsync(handler, "scan", site, "--target", "17.7.2", "--format", "json", "--no-inspect", "--private-ids", ids);

        Assert.Equal(0, r.Code);
        var pkg = Json(r).GetProperty("sites")[0].GetProperty("packages").EnumerateArray().Single(p => p.GetProperty("id").GetString() == Contentment);
        Assert.Equal("unknown", pkg.GetProperty("verdict").GetString());
        Assert.Equal("private-feed", pkg.GetProperty("reason").GetString());
        Assert.DoesNotContain(handler.Requests, u => u.Contains("contentment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CurrentAndNextMajor_ResolvePerSite()
    {
        var v13 = TestSites.Create("13.5.2");
        var v17 = TestSites.Create("17.7.2");

        var current = Json(await CliHarness.RunAsync(TestSites.Handler(), "scan", v13, v17, "--target", "current", "--format", "json", "--no-inspect"));
        var next = Json(await CliHarness.RunAsync(TestSites.Handler(), "scan", v13, v17, "--target", "next-major", "--format", "json", "--no-inspect"));

        string Resolved(JsonElement root, int i) => root.GetProperty("sites")[i].GetProperty("target").GetProperty("resolved").GetString()!;
        Assert.Equal("13.5.2", Resolved(current, 0));
        Assert.Equal("17.7.2", Resolved(current, 1));
        Assert.StartsWith("14.", Resolved(next, 0), StringComparison.Ordinal);
        Assert.StartsWith("18.", Resolved(next, 1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UmbracoCmsItself_IsNotJudged()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.Create("17.7.2"), "--target", "17.7.2", "--format", "json", "--no-inspect");

        Assert.Equal(0, Json(r).GetProperty("sites")[0].GetProperty("packages").GetArrayLength());
    }

    [Fact]
    public async Task OutputFile_WritesJson_AndConsoleSummaryStillPrinted()
    {
        var site = TestSites.Create("17.7.2", (Contentment, "6.2.1"));
        var file = Path.Combine(site, "out", "report.json");

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", site, "--target", "17.7.2", "--format", "json", "-o", file, "--no-inspect");

        Assert.Equal(0, r.Code);
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("Umbraco Package Check", r.Console, StringComparison.Ordinal);
        Assert.Contains(Contentment, r.Console, StringComparison.Ordinal);
        Assert.Equal("", r.Stdout);
    }

    [Fact]
    public async Task OutputWithConsoleFormat_Exit2()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.Create("17.7.2"), "-o", "x.json");

        Assert.Equal(2, r.Code);
        Assert.Contains("--output", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflineNoCache_Exit2()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.Create("17.7.2"), "--offline", "--no-cache");

        Assert.Equal(2, r.Code);
        Assert.Contains("--offline", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownOption_Exit2()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", "--bogus");

        Assert.Equal(2, r.Code);
        Assert.NotEqual("", r.Stderr);
    }

    [Fact]
    public async Task NoInspect_NoNupkgRequested()
    {
        var gp = TestSites.SharedGlobalPackages();
        var handler = TestSites.Handler();

        var r = await CliHarness.RunAsync(handler, "scan", TestSites.Create("17.7.2", gp, (Contentment, "6.2.1")), "--target", "17.7.2", "--no-inspect", "--no-cache");

        Assert.Equal(0, r.Code);
        Assert.DoesNotContain(handler.Requests, u => u.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateFileSystemEntries(gp));
    }

    [Fact]
    public async Task Offline_WarmCache_MakesNoHttp_AndKeepsVerdicts()
    {
        // Design 6.1 AC, through the real composition (review M4 F4, M4-S11).
        var cache = Directory.CreateTempSubdirectory("pc-cli-offline-").FullName;
        var site = TestSites.Create("17.7.2", (Contentment, "5.2.1"), ("Astroboard", "1.1.3"));
        string[] args = ["scan", site, "--target", "17.7.2", "--no-inspect", "--format", "json", "--cache-dir", cache];

        var warm = await CliHarness.RunAsync(TestSites.Handler(), args);
        var offlineHandler = TestSites.Handler();
        var offline = await CliHarness.RunAsync(offlineHandler, [.. args, "--offline"]);

        Assert.Equal(0, warm.Code);
        Assert.Equal(0, offline.Code);
        Assert.Equal(0, offlineHandler.RequestCount);
        static string Verdicts(CliResult r) => string.Join(",", JsonDocument.Parse(r.Stdout).RootElement.GetProperty("sites")[0].GetProperty("packages")
            .EnumerateArray().Select(p => $"{p.GetProperty("id").GetString()}={p.GetProperty("verdict").GetString()}/{p.GetProperty("reason")}"));
        Assert.Equal(Verdicts(warm), Verdicts(offline));
        Assert.DoesNotContain("offline", Verdicts(offline), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PositiveControls_InspectAndLookupDoRequest()
    {
        // R12: the no-request tests above only mean something if the same scan without the switch does request.
        var gp = TestSites.SharedGlobalPackages();
        var handler = TestSites.Handler();

        await CliHarness.RunAsync(handler, "scan", TestSites.Create("17.7.2", gp, (Contentment, "6.2.1")), "--target", "17.7.2", "--no-cache", "--format", "json");

        Assert.Contains(handler.Requests, u => u.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, u => u.Contains("contentment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoCache_LeavesNoHttpCacheFolder()
    {
        // Review M4 F8 / M4-S10: --no-cache must not leave a temp folder per run.
        using var provider = Commands.ServiceRegistration.Build(
            new CliEnvironment(TextWriter.Null, TextWriter.Null, new Spectre.Console.Testing.TestConsole(), null, TimeProvider.System, null),
            new Commands.ScanRequest([], "17.7.2", Core.FailOn.None, false, false, new ScanOptions(false, false, false), false, null, null, TimeSpan.FromHours(6), NoCache: true));

        Assert.Null(provider.GetRequiredService<Commands.SourceMode>().HttpCacheFolder);
    }

    [Fact]
    public async Task CacheDir_IsUsed()
    {
        var cache = Path.Combine(Directory.CreateTempSubdirectory("pc-cli-cachedir-").FullName, "c");

        var r = await CliHarness.RunWithAsync(TestSites.Handler(), null, null,
            "scan", TestSites.Create("17.7.2", (Contentment, "6.2.1")), "--target", "17.7.2", "--no-inspect", "--cache-dir", cache);

        Assert.Equal(0, r.Code);
        Assert.NotEmpty(Directory.EnumerateFiles(cache, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SameIdVersionAcrossSites_FetchedOnce()
    {
        var gp = TestSites.SharedGlobalPackages();
        var handler = TestSites.Handler();

        var r = await CliHarness.RunAsync(handler, "scan",
            TestSites.Create("17.7.2", gp, (Contentment, "6.2.1")), TestSites.Create("17.7.2", gp, (Contentment, "6.2.1")),
            "--target", "17.7.2", "--no-inspect", "--no-cache", "--format", "json");

        Assert.Equal(0, r.Code);
        Assert.Equal(1, handler.CountFor("https://api.nuget.org/v3/registration5-gz-semver2/umbraco.community.contentment/index.json"));
    }

    [Fact]
    public async Task Concurrency_NeverExceedsEight()
    {
        var packages = Enumerable.Range(1, 20).Select(i => ($"PcFake.Pkg{i:00}", "1.0.0")).ToArray();
        var handler = TestSites.Handler()
            .Route("https://api.nuget.org/v3/registration5-gz-semver2/pcfake.", async (_, ct) =>
            {
                await Task.Delay(50, ct);
                return FixtureHttpHandler.Json(HttpStatusCode.NotFound, null);
            })
            .Route("https://api.marketplace.umbraco.com/api/v1.0/packages/PcFake.", HttpStatusCode.NotFound);

        var r = await CliHarness.RunAsync(handler, "scan", TestSites.Create("17.7.2", packages), "--target", "17.7.2", "--no-inspect", "--no-cache", "--format", "json");

        Assert.Equal(0, r.Code);
        Assert.InRange(handler.MaxInFlight, 2, 8);
    }

    [Fact]
    public async Task SiteWarnings_AppearInConsoleJsonAndMd()
    {
        var site = TestSites.FromFixture("sln-missing-project");
        File.WriteAllText(Path.Combine(site, "Site", "Site.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Umbraco.Cms" Version="17.7.2" /></ItemGroup>
            </Project>
            """);
        string[] Args(string format) => ["scan", site, "--target", "17.7.2", "--no-inspect", "--format", format];

        var console = await CliHarness.RunAsync(TestSites.Handler(), Args("console"));
        var json = await CliHarness.RunAsync(TestSites.Handler(), Args("json"));
        var md = await CliHarness.RunAsync(TestSites.Handler(), Args("md"));

        Assert.Contains("Gone.csproj", console.Console, StringComparison.Ordinal);
        Assert.Contains("Gone.csproj", Json(json).GetProperty("sites")[0].GetProperty("warnings")[0].GetString(), StringComparison.Ordinal);
        Assert.Contains("Gone\\.csproj", md.Stdout, StringComparison.Ordinal);
    }
}
