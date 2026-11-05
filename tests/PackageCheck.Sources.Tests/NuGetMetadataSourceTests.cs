using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using NuGet.Versioning;
using PackageCheck.Core;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

public class NuGetMetadataSourceTests
{
    private const string Contentment = "Umbraco.Community.Contentment";
    private const string FeedA = "https://feed-a.test";
    private const string FeedB = "https://feed-b.test";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NuGetOrgOnly(TestFeeds f) => f.WriteConfig("site", [("nuget.org", TestFeeds.NuGetOrg)]);

    [Fact]
    public async Task Versions_IncludeUnlisted()
    {
        using var f = new TestFeeds();
        var source = f.Source(NuGetOrgOnly(f));

        var versions = await source.GetVersionsAsync(Contentment, Ct);

        // 6.0.1 is unlisted on nuget.org (recorded 2026-11-03); the list must still carry it.
        Assert.Contains(NuGetVersion.Parse("6.0.1"), versions.Versions);
        Assert.Equal(versions.Versions.OrderBy(v => v), versions.Versions);
        var details = await source.GetDetailsAsync(Contentment, NuGetVersion.Parse("6.0.1"), Ct);
        Assert.NotNull(details);
        Assert.False(details.Listed);
        Assert.Null(details.Published); // nuget.org's 1900-01-01 unlisted sentinel is not a release date
        Assert.Empty(f.Handler.Misses);
    }

    [Fact]
    public async Task Details_HaveDependencyGroupsPerTfm()
    {
        using var f = new TestFeeds();
        var source = f.Source(NuGetOrgOnly(f));

        var details = await source.GetDetailsAsync(Contentment, NuGetVersion.Parse("6.2.1"), Ct);

        Assert.NotNull(details);
        Assert.True(details.Listed);
        Assert.NotNull(details.Published);
        var net10 = Assert.Single(details.DependencyGroups, g => g.TargetFramework.GetShortFolderName() == "net10.0");
        var core = Assert.Single(net10.Packages, p => p.Id == "Umbraco.Cms.Core");
        Assert.Equal(VersionRange.Parse("[17.0.0, 18.0.0)"), core.VersionRange);
        Assert.Contains(details.DependencyGroups, g => g.TargetFramework.GetShortFolderName() == "net9.0");
    }

    [Fact]
    public async Task Details_UnknownVersion_IsNull()
    {
        using var f = new TestFeeds();
        var source = f.Source(NuGetOrgOnly(f));

        Assert.Null(await source.GetDetailsAsync(Contentment, NuGetVersion.Parse("99.0.0"), Ct));
    }

    [Fact]
    public async Task Details_DeprecationWithAlternate()
    {
        using var f = new TestFeeds();
        var source = f.Source(NuGetOrgOnly(f));

        var details = await source.GetDetailsAsync("Plumber.Workflow", NuGetVersion.Parse("10.1.2"), Ct);

        Assert.NotNull(details?.Deprecation);
        Assert.Equal("Umbraco.Workflow", details.Deprecation.AlternateId, ignoreCase: true);
        Assert.Contains("Legacy", details.Deprecation.Reasons);
    }

    [Fact]
    public async Task Versions_IdComparesCaseInsensitively()
    {
        using var f = new TestFeeds();
        var source = f.Source(NuGetOrgOnly(f));

        var lower = await source.GetVersionsAsync(Contentment.ToLowerInvariant(), Ct);

        Assert.NotEmpty(lower.Versions);
        Assert.Equal(Contentment.ToLowerInvariant(), lower.Id); // reports keep the id as the project wrote it
    }

    [Fact]
    public async Task NotFoundOnAllFeeds_ThrowsNotFound()
    {
        using var f = new TestFeeds();
        f.SyntheticFeed(FeedA, "404");
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]));

        var e = await Assert.ThrowsAsync<PackageSourceException>(() => source.GetVersionsAsync("No.Such.Package", Ct));

        Assert.Equal(UnknownReason.NotFound, e.Reason);
    }

    [Theory]
    [InlineData("401")]
    [InlineData("403")]
    public async Task FeedDenied_ThrowsPrivateFeed(string kind)
    {
        using var f = new TestFeeds();
        f.SyntheticFeed(FeedA, kind);
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]));

        var e = await Assert.ThrowsAsync<PackageSourceException>(() => source.GetVersionsAsync("Private.Package", Ct));

        Assert.Equal(UnknownReason.PrivateFeed, e.Reason);
    }

    [Fact]
    public Task Feed401_ThrowsPrivateFeed() => FeedDenied_ThrowsPrivateFeed("401");

    [Fact]
    public Task Feed403_ThrowsPrivateFeed() => FeedDenied_ThrowsPrivateFeed("403");

    [Fact]
    public async Task ConnectionRefused_ThrowsSourceError()
    {
        using var f = new TestFeeds();
        f.SyntheticFeed(FeedA, "refused");
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]));

        var e = await Assert.ThrowsAsync<PackageSourceException>(() => source.GetVersionsAsync("Any.Package", Ct));

        Assert.Equal(UnknownReason.SourceError, e.Reason);
    }

    /// <summary>Design 7.3 multi-feed table: per package, across every mapped feed, first feed holding it wins.</summary>
    [Theory]
    [InlineData("404", "401", "private-feed")]
    [InlineData("404", "200", "found")]
    [InlineData("401", "200", "found")]
    [InlineData("5xx", "200", "found")]
    [InlineData("refused", "200", "found")]
    [InlineData("5xx", "404", "source-error")]
    [InlineData("404", "404", "not-found")]
    public async Task MixedFeeds_Matrix(string feedA, string feedB, string expected)
    {
        using var f = new TestFeeds();
        var clock = new FakeTimeProvider();
        f.SyntheticFeed(FeedA, feedA);
        string feedBUrl;
        if (feedB == "200")
        {
            feedBUrl = TestFeeds.NuGetOrg; // the recorded nuget.org responses stand in for a feed that has the package
        }
        else
        {
            f.SyntheticFeed(FeedB, feedB);
            feedBUrl = $"{FeedB}/v3/index.json";
        }
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json"), ("b", feedBUrl)]), clock);

        var work = source.GetVersionsAsync(Contentment, Ct);

        if (expected == "found")
        {
            var versions = await TestFeeds.Drive(clock, work);
            Assert.NotEmpty(versions.Versions);
        }
        else
        {
            var e = Assert.IsType<PackageSourceException>(await TestFeeds.DriveToFailure(clock, work));
            Assert.Equal(expected, e.Reason.ToWireValue());
        }
        Assert.Empty(f.Handler.Misses);
    }

    [Fact]
    public async Task SuccessfulFeed_IsUsedForDetailsWithoutRequeryingFailedFeeds()
    {
        using var f = new TestFeeds();
        f.SyntheticFeed(FeedA, "404");
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json"), ("nuget.org", TestFeeds.NuGetOrg)]));

        await source.GetVersionsAsync(Contentment, Ct);
        var before = f.Handler.RequestCount;
        await source.GetDetailsAsync(Contentment, NuGetVersion.Parse("6.2.1"), Ct);

        Assert.Equal(before, f.Handler.RequestCount); // one registration read per id per run
    }

    [Fact]
    public async Task NearestNuGetConfig_Wins()
    {
        // Preflight 1.18 / design 7.3: the config nearest the site wins, not one nearer to a project below it.
        using var f = new TestFeeds();
        f.WriteConfig(".", [("outer", "https://outer.test/v3/index.json")]);
        var site = f.WriteConfig("site", [("site-feed", $"{FeedA}/v3/index.json")]);
        f.WriteConfig(Path.Combine("site", "src"), [("project-feed", "https://project.test/v3/index.json")]);

        var settings = f.Settings(site);

        Assert.Equal(["site-feed"], settings.Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task NearestNuGetConfig_ProjectFeedIsNeverQueried()
    {
        using var f = new TestFeeds();
        f.SyntheticFeed(FeedA, "404");
        f.SyntheticFeed("https://project.test", "404");
        var site = f.WriteConfig("site", [("site-feed", $"{FeedA}/v3/index.json")]);
        f.WriteConfig(Path.Combine("site", "src"), [("project-feed", "https://project.test/v3/index.json")]);

        await Assert.ThrowsAsync<PackageSourceException>(() => f.Source(site).GetVersionsAsync("X.Y", Ct));

        Assert.Equal(0, f.Handler.CountFor("https://project.test"));
        Assert.True(f.Handler.CountFor(FeedA) > 0);
    }

    [Fact]
    public async Task SourceMapping_UnmappedFeed_NeverQueried()
    {
        using var f = new TestFeeds();
        f.SyntheticFeed("https://dummy.test", "404");
        var site = f.WriteConfig("site",
            [("nuget.org", TestFeeds.NuGetOrg), ("dummy", "https://dummy.test/v3/index.json")],
            """
              <packageSourceMapping>
                <packageSource key="nuget.org"><package pattern="Umbraco.*" /></packageSource>
                <packageSource key="dummy"><package pattern="Internal.*" /></packageSource>
              </packageSourceMapping>
            """);
        var source = f.Source(site);

        var versions = await source.GetVersionsAsync(Contentment, Ct);

        Assert.NotEmpty(versions.Versions);
        Assert.Equal(0, f.Handler.CountFor("https://dummy.test"));

        // Positive control: an id mapped to the dummy feed does reach it, so the zero above means something.
        await Assert.ThrowsAsync<PackageSourceException>(() => source.GetVersionsAsync("Internal.Thing", Ct));
        Assert.True(f.Handler.CountFor("https://dummy.test") > 0);
    }

    [Fact]
    public async Task Timeout_And_5xxAfterRetries_ThrowSourceError()
    {
        // Two failure kinds, one rule: HttpPolicy makes exactly 3 attempts and NuGet.Protocol adds none (preflight row 12).
        using var f = new TestFeeds();
        var clock = new FakeTimeProvider();
        f.SyntheticFeed(FeedA, "5xx");
        f.Handler.Route(FeedB, async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct); // never answers; only the 30 s per-call timeout ends it
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var five = f.Source(f.WriteConfig("a", [("a", $"{FeedA}/v3/index.json")]), clock);
        var e1 = Assert.IsType<PackageSourceException>(await TestFeeds.DriveToFailure(clock, five.GetVersionsAsync("Any.Id", Ct)));
        Assert.Equal(UnknownReason.SourceError, e1.Reason);
        Assert.Equal(3, f.Handler.CountFor(FeedA));

        var hang = f.Source(f.WriteConfig("b", [("b", $"{FeedB}/v3/index.json")]), clock);
        var e2 = Assert.IsType<PackageSourceException>(await TestFeeds.DriveToFailure(clock, hang.GetVersionsAsync("Any.Id", Ct)));
        Assert.Equal(UnknownReason.SourceError, e2.Reason);
        Assert.Equal(3, f.Handler.CountFor(FeedB));
    }

    [Theory]
    [InlineData("reg")]
    [InlineData("flat")]
    public async Task ResourceLevel503_IsTriedExactlyThreeTimes(string resource)
    {
        // Each NuGet.Protocol resource can carry its own retry loop; none may stack on HttpPolicy (preflight row 12).
        using var f = new TestFeeds();
        var clock = new FakeTimeProvider();
        f.Handler.Route($"{FeedA}/v3/index.json", HttpStatusCode.OK, TestFeeds.ServiceIndex(FeedA));
        f.Handler.Route($"{FeedA}/{resource}/", HttpStatusCode.ServiceUnavailable);
        f.Handler.Route($"{FeedA}/", HttpStatusCode.NotFound);
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]), clock);

        Task<object?> work = resource == "reg"
            ? source.GetVersionsAsync("Any.Id", Ct).ContinueWith(t => (object?)t.Result, Ct)
            : source.OpenNupkgAsync("Any.Id", NuGetVersion.Parse("1.0.0"), Ct).ContinueWith(t => (object?)t.Result, Ct);
        var e = await TestFeeds.DriveToFailure(clock, work);

        var pse = Assert.IsType<PackageSourceException>(e is AggregateException agg ? agg.InnerException : e);
        Assert.Equal(UnknownReason.SourceError, pse.Reason);
        Assert.Equal(3, f.Handler.CountFor($"{FeedA}/{resource}/"));
        Assert.Equal(1, f.Handler.CountFor($"{FeedA}/v3/index.json"));
    }

    [Fact]
    public async Task Transient503_ThenSuccess_IsFound()
    {
        using var f = new TestFeeds();
        var clock = new FakeTimeProvider();
        var calls = 0;
        f.Handler.Route($"{FeedA}/v3/index.json", (_, _) => Task.FromResult(Interlocked.Increment(ref calls) < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : FixtureHttpHandler.Json(HttpStatusCode.OK, TestFeeds.ServiceIndex(FeedA))));
        f.Handler.Route($"{FeedA}/", HttpStatusCode.NotFound);
        // The third index attempt succeeds, so the outcome is the feed's real answer (not-found), not source-error.
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]), clock);

        var e = Assert.IsType<PackageSourceException>(await TestFeeds.DriveToFailure(clock, source.GetVersionsAsync(Contentment, Ct)));

        Assert.Equal(UnknownReason.NotFound, e.Reason);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Nupkg_GlobalFolderFirst_NoHttp()
    {
        using var f = new TestFeeds();
        var dir = Path.Combine(f.GlobalPackages, "umbraco.community.contentment", "6.2.1");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "umbraco.community.contentment.6.2.1.nupkg");
        await File.WriteAllBytesAsync(file, new byte[123], Ct);
        var source = f.Source(NuGetOrgOnly(f));

        await using var handle = await source.OpenNupkgAsync(Contentment, NuGetVersion.Parse("6.2.1"), Ct);

        Assert.NotNull(handle);
        Assert.Equal(file, handle.FilePath);
        Assert.False(handle.IsTemporary);
        Assert.Equal(123, handle.Length);
        Assert.Equal(0, f.Handler.RequestCount);
        await handle.DisposeAsync();
        Assert.True(File.Exists(file)); // never delete from the user's global packages folder
    }

    [Fact]
    public async Task Nupkg_GlobalFolderOverCap_ThrowsTooLarge_NotOpened()
    {
        using var f = new TestFeeds();
        var dir = Path.Combine(f.GlobalPackages, "big.pkg", "1.0.0");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "big.pkg.1.0.0.nupkg"), new byte[2048], Ct);
        var source = f.Source(NuGetOrgOnly(f), limits: new ArchiveLimits(MaxNupkgBytes: 1024));

        await Assert.ThrowsAsync<NupkgTooLargeException>(() => source.OpenNupkgAsync("Big.Pkg", NuGetVersion.Parse("1.0.0"), Ct));

        Assert.Equal(0, f.Handler.RequestCount);
    }

    private static void FlatContainerFeed(TestFeeds f, string id, string version, byte[] nupkg)
    {
        var lid = id.ToLowerInvariant();
        f.Handler.Route($"{FeedA}/v3/index.json", HttpStatusCode.OK, TestFeeds.ServiceIndex(FeedA));
        f.Handler.Route($"{FeedA}/flat/{lid}/index.json", HttpStatusCode.OK, $$"""{ "versions": ["{{version}}"] }""");
        f.Handler.Route($"{FeedA}/flat/{lid}/{version}/{lid}.{version}.nupkg", (_, _) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(nupkg) };
            res.Content.Headers.ContentType = new("application/octet-stream");
            return Task.FromResult(res);
        });
        f.Handler.Route($"{FeedA}/", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nupkg_Download_IsTemporary_AndDeletedOnDispose()
    {
        using var f = new TestFeeds();
        var bytes = Encoding.ASCII.GetBytes("PK-not-really-a-zip-but-bytes-are-bytes");
        FlatContainerFeed(f, "Small.Pkg", "1.0.0", bytes);
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]));

        var handle = await source.OpenNupkgAsync("Small.Pkg", NuGetVersion.Parse("1.0.0"), Ct);

        Assert.NotNull(handle);
        Assert.True(handle.IsTemporary);
        Assert.Equal(bytes.Length, handle.Length);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(handle.FilePath, Ct));
        Assert.StartsWith(f.Temp, handle.FilePath);
        await handle.DisposeAsync();
        Assert.False(File.Exists(handle.FilePath));
    }

    [Fact]
    public async Task Nupkg_VersionAbsentOnFeed_IsNull()
    {
        using var f = new TestFeeds();
        FlatContainerFeed(f, "Small.Pkg", "1.0.0", new byte[10]);
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]));

        Assert.Null(await source.OpenNupkgAsync("Small.Pkg", NuGetVersion.Parse("2.0.0"), Ct));
    }

    [Fact]
    public async Task Nupkg_OverCap_ThrowsTooLarge_AndTempDeleted()
    {
        using var f = new TestFeeds();
        FlatContainerFeed(f, "Big.Pkg", "1.0.0", new byte[4096]);
        var source = f.Source(f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")]), limits: new ArchiveLimits(MaxNupkgBytes: 1024));

        var e = await Assert.ThrowsAsync<NupkgTooLargeException>(() => source.OpenNupkgAsync("Big.Pkg", NuGetVersion.Parse("1.0.0"), Ct));

        Assert.Equal(1024, e.Limit);
        Assert.True(!Directory.Exists(f.Temp) || Directory.GetFiles(f.Temp, "*", SearchOption.AllDirectories).Length == 0);
    }

    [Fact]
    public async Task Credentials_NeverInExceptionMessages()
    {
        using var f = new TestFeeds();
        f.SyntheticFeed(FeedA, "401");
        var site = f.WriteConfig("site",
            [("a", "https://leaky:SECRET-IN-URL@feed-a.test/v3/index.json?token=SECRET-IN-QUERY")],
            """
              <packageSourceCredentials>
                <a>
                  <add key="Username" value="planted-user" />
                  <add key="ClearTextPassword" value="PLANTED-TOKEN-123" />
                </a>
              </packageSourceCredentials>
            """);
        var source = f.Source(site);

        var e = await Assert.ThrowsAsync<PackageSourceException>(() => source.GetVersionsAsync("Private.Package", Ct));

        Assert.Equal(UnknownReason.PrivateFeed, e.Reason);
        var text = e.ToString();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("planted-user:PLANTED-TOKEN-123"));
        foreach (var secret in new[] { "PLANTED-TOKEN-123", basic, "SECRET-IN-URL", "SECRET-IN-QUERY", "leaky" })
            Assert.DoesNotContain(secret, text);
        Assert.Contains("feed-a.test", text); // the feed is still named, redacted
    }

    [Theory]
    [InlineData("401")]
    [InlineData("5xx")]
    [InlineData("refused")]
    [InlineData("404")]
    public async Task FeedUrlSecrets_NeverInExceptionMessages_AnyOutcome(string kind)
    {
        using var f = new TestFeeds();
        var clock = new FakeTimeProvider();
        f.SyntheticFeed(FeedA, kind);
        var site = f.WriteConfig("site", [("a", "https://leaky:SECRET-IN-URL@feed-a.test/v3/index.json?sig=SECRET-IN-QUERY")]);

        var e = await TestFeeds.DriveToFailure(clock, f.Source(site, clock).GetVersionsAsync("Private.Package", Ct));

        var text = e.ToString();
        Assert.IsType<PackageSourceException>(e);
        Assert.DoesNotContain("SECRET-IN-URL", text);
        Assert.DoesNotContain("SECRET-IN-QUERY", text);
        Assert.DoesNotContain("leaky", text);
    }

    [Fact]
    public async Task Credentials_NeverWrittenToTheHttpCacheOrTemp()
    {
        using var f = new TestFeeds();
        f.Handler.Route($"{FeedA}/v3/index.json", HttpStatusCode.OK, TestFeeds.ServiceIndex(FeedA));
        f.Handler.Route($"{FeedA}/", HttpStatusCode.NotFound);
        var site = f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")], """
              <packageSourceCredentials>
                <a>
                  <add key="Username" value="user" />
                  <add key="ClearTextPassword" value="PLANTED-TOKEN-123" />
                </a>
              </packageSourceCredentials>
            """);

        await Assert.ThrowsAsync<PackageSourceException>(() => f.Source(site).GetVersionsAsync("X.Y", Ct));
        Assert.NotNull(f.Handler.Authorizations[0]); // positive control: the credentials were in play

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:PLANTED-TOKEN-123"));
        foreach (var dir in new[] { f.HttpCache, f.Temp }.Where(Directory.Exists))
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                var content = await File.ReadAllTextAsync(file, Ct);
                Assert.DoesNotContain("PLANTED-TOKEN-123", content);
                Assert.DoesNotContain(basic, content);
            }
    }

    [Fact]
    public async Task Credentials_FromNuGetConfig_SentAsBasicAuth_WithEnvExpansion()
    {
        using var f = new TestFeeds();
        f.SyntheticFeed(FeedA, "404");
        var variable = "PACKAGECHECK_TEST_TOKEN_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "from-env-456");
        try
        {
            var site = f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")], $"""
                  <packageSourceCredentials>
                    <a>
                      <add key="Username" value="user" />
                      <add key="ClearTextPassword" value="%{variable}%" />
                    </a>
                  </packageSourceCredentials>
                """);

            await Assert.ThrowsAsync<PackageSourceException>(() => f.Source(site).GetVersionsAsync("X.Y", Ct));

            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:from-env-456"));
            Assert.Equal(expected, f.Handler.Authorizations[0]);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Fact]
    public async Task Credentials_NeverSentToAnotherHost()
    {
        // A service index may point resources at another host; the feed's credentials must not follow it there.
        using var f = new TestFeeds();
        f.Handler.Route($"{FeedA}/v3/index.json", HttpStatusCode.OK, TestFeeds.ServiceIndex(FeedA, registrationHost: "https://other.test"));
        f.Handler.Route("https://other.test/", HttpStatusCode.NotFound);
        var site = f.WriteConfig("site", [("a", $"{FeedA}/v3/index.json")], """
              <packageSourceCredentials>
                <a>
                  <add key="Username" value="user" />
                  <add key="ClearTextPassword" value="pw" />
                </a>
              </packageSourceCredentials>
            """);

        await Assert.ThrowsAsync<PackageSourceException>(() => f.Source(site).GetVersionsAsync("X.Y", Ct));

        var pairs = f.Handler.Requests.Zip(f.Handler.Authorizations).ToList();
        Assert.Contains(pairs, p => p.First.StartsWith(FeedA) && p.Second is not null);
        Assert.Contains(pairs, p => p.First.StartsWith("https://other.test"));
        Assert.All(pairs.Where(p => p.First.StartsWith("https://other.test")), p => Assert.Null(p.Second));
    }

    [Fact]
    public async Task UserAgent_IsSet()
    {
        using var f = new TestFeeds();
        var source = f.Source(NuGetOrgOnly(f));

        await source.GetVersionsAsync(Contentment, Ct);

        Assert.NotEmpty(f.Handler.UserAgents);
        Assert.All(f.Handler.UserAgents, ua => Assert.Equal(
            $"umbraco-package-check/{ToolInfo.Version} (+https://github.com/PascalA94/Umbraco.Community.PackageCheck)", ua));
    }

    // Security review M3 (S5): ids come from project files and feeds. An id that is not a valid NuGet id never reaches a
    // path or URL: "../../x" or "/tmp/x" made VersionFolderPathResolver point outside the global packages folder.
    [Theory]
    [InlineData("../../../../tmp/evil")]
    [InlineData("/tmp/evil")]
    [InlineData("a b")]
    public async Task InvalidId_IsRejected_WithoutIoOrHttp(string id)
    {
        using var f = new TestFeeds();
        var site = f.WriteConfig("site", [("nuget.org", TestFeeds.NuGetOrg)]);
        using var source = f.Source(site);
        var v = NuGetVersion.Parse("1.0.0");
        foreach (var call in new Func<Task>[]
                 {
                     () => source.GetVersionsAsync(id, Ct), () => source.GetDetailsAsync(id, v, Ct), () => source.OpenNupkgAsync(id, v, Ct),
                 })
        {
            var ex = await Assert.ThrowsAsync<PackageCheck.Core.Facts.PackageSourceException>(call);
            Assert.Equal(PackageCheck.Core.Model.UnknownReason.NotFound, ex.Reason);
            Assert.DoesNotContain(id, ex.Message);
        }
        Assert.Equal(0, f.Handler.RequestCount);
    }

    [Fact]
    public void DefaultTempFolder_IsPrivateAndPerRun() // security review M3 (S6)
    {
        var settings = global::NuGet.Configuration.Settings.LoadDefaultSettings(Path.GetTempPath());
        var a = PackageCheck.Sources.NuGet.FeedSettings.FromSettings(settings, "/g", "/h");
        var b = PackageCheck.Sources.NuGet.FeedSettings.FromSettings(settings, "/g", "/h");
        Assert.NotEqual(a.TempFolder, b.TempFolder);
        Assert.True(Directory.Exists(a.TempFolder));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(a.TempFolder));
        Directory.Delete(a.TempFolder);
        Directory.Delete(b.TempFolder);
    }

    [Fact]
    public async Task RegistrationForAnotherId_IsIgnored() // security review M3 (S5): the feed's id later builds paths
    {
        using var f = new TestFeeds();
        // A real recorded registration (Umbraco.Community.Contentment) served as if it were Pkg's.
        var recorded = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Directory.GetFiles(TestFeeds.NuGetFixtures,
            "api.nuget.org_v3_registration5-gz-semver2_umbraco.community.contentment_index.json_*.json").Single()))!;
        var body = recorded["body"]!.GetValue<string>();
        f.Handler.Route("https://api.nuget.org/v3/registration5-gz-semver2/pkg/index.json", HttpStatusCode.OK, body);
        var site = f.WriteConfig("site", [("nuget.org", TestFeeds.NuGetOrg)]);
        using var source = f.Source(site);
        var ex = await Assert.ThrowsAsync<PackageSourceException>(() => source.GetVersionsAsync("Pkg", Ct));
        Assert.Equal(UnknownReason.NotFound, ex.Reason);
    }
}

