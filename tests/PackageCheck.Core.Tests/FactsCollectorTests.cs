using NuGet.Frameworks;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests;

public class FactsCollectorTests
{
    private static readonly FactsOptions Options = new(Inspect: false, ProjectDirectories: ["src/Site"]);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static Task<PackageFacts> Collect(FakeFeed feed, string id, string version, string target = "17.7.2") =>
        new FactsCollector(feed).CollectAsync(id, NuGetVersion.Parse(version), Target(target), Options, Ct);

    [Fact]
    public async Task DirectUmbracoDependency_Depth1()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)", "Newtonsoft.Json [13.0.1, )");
        var f = await Collect(feed, "Pkg", "1.0.0");
        var u = Assert.Single(f.Umbraco);
        Assert.Equal(("Pkg", "Umbraco.Cms.Core", 1), (u.ViaPackageId, u.UmbracoPackageId, u.Depth));
        Assert.Equal(VersionRange.Parse("[17.0.0, 18.0.0)"), u.Range);
        Assert.Equal(["Umbraco.Cms.Core", "Newtonsoft.Json"], f.DependencyIds);
        Assert.Empty(f.UmbracoPathDependencies);
    }

    [Fact]
    public async Task TransitiveUmbracoDependency_FoundAtDepth2()
    {
        var feed = new FakeFeed()
            .Add("A", "1.0.0", "jcdcdev.Umbraco.Core [17.1.0, )")
            .Add("jcdcdev.Umbraco.Core", "17.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .Add("jcdcdev.Umbraco.Core", "17.1.0", "Umbraco.Cms.Core [17.1.0, 18.0.0)")
            .Add("jcdcdev.Umbraco.Core", "17.2.0", "Umbraco.Cms.Core [17.2.0, 18.0.0)");
        var f = await Collect(feed, "A", "1.0.0");
        var u = Assert.Single(f.Umbraco);
        // NuGet's lowest-applicable rule: [17.1.0, ) resolves jcdcdev.Umbraco.Core 17.1.0.
        Assert.Equal(("jcdcdev.Umbraco.Core", 2, "[17.1.0, 18.0.0)"), (u.ViaPackageId, u.Depth, u.Range.ToNormalizedString()));
        Assert.Equal("jcdcdev.Umbraco.Core", Assert.Single(f.UmbracoPathDependencies).Id);
    }

    [Fact]
    public async Task Depth3_IsTheLimit_AndIsReached()
    {
        // Contentment 4.7.0's shape: shim -> Our -> Our.Core -> Umbraco.Cms.Web.Website (ruling R3).
        var feed = new FakeFeed()
            .Add("Shim", "1.0.0", "Our [1.0.0, )")
            .Add("Our", "1.0.0", "Our.Core [1.0.0, )")
            .Add("Our.Core", "1.0.0", "Umbraco.Cms.Web.Website [10.0.0, 14.0.0)");
        var f = await Collect(feed, "Shim", "1.0.0");
        Assert.Equal(3, Assert.Single(f.Umbraco).Depth);
    }

    [Fact]
    public async Task NoPathWithinDepth3_UmbracoIsEmpty()
    {
        var feed = new FakeFeed()
            .Add("A", "1.0.0", "B [1.0.0, )")
            .Add("B", "1.0.0", "C [1.0.0, )")
            .Add("C", "1.0.0", "D [1.0.0, )")
            .Add("D", "1.0.0", "Umbraco.Cms.Core [17.0.0, )");
        var f = await Collect(feed, "A", "1.0.0");
        Assert.Empty(f.Umbraco);
        Assert.Empty(f.UmbracoPathDependencies);
    }

    [Fact]
    public async Task DirectRange_StopsTheWalkForRanges_ButStillFindsUmbracoDependencies()
    {
        var feed = new FakeFeed()
            .Add("Umbraco.Workflow", "17.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)", "Umbraco.Workflow.StaticAssets [17.0.0]")
            .Add("Umbraco.Workflow.StaticAssets", "17.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)");
        var f = await Collect(feed, "Umbraco.Workflow", "17.0.0");
        Assert.Equal(1, Assert.Single(f.Umbraco).Depth);
        Assert.Equal("Umbraco.Workflow.StaticAssets", Assert.Single(f.UmbracoPathDependencies).Id);
    }

    [Fact]
    public async Task IntegrationsPackages_AreNotTheCms()
    {
        var feed = new FakeFeed()
            .Add("A", "1.0.0", "Umbraco.Cms.Integrations.Crm.Hubspot [1.0.0, )")
            .Add("Umbraco.Cms.Integrations.Crm.Hubspot", "1.0.0", "Umbraco.Cms.Web.Common [17.0.0, 18.0.0)");
        var f = await Collect(feed, "A", "1.0.0");
        var u = Assert.Single(f.Umbraco);
        Assert.Equal(("Umbraco.Cms.Integrations.Crm.Hubspot", "Umbraco.Cms.Web.Common", 2), (u.ViaPackageId, u.UmbracoPackageId, u.Depth));
    }

    [Fact]
    public async Task GroupChosen_IsNearestForTargetTfm()
    {
        var feed = new FakeFeed().Add("Pkg", "6.2.1",
        [
            ("net8.0", ["Umbraco.Cms.Core [13.0.0, 14.0.0)"]),
            ("net10.0", ["Umbraco.Cms.Core [17.0.0, 18.0.0)"]),
        ]);
        var f = await Collect(feed, "Pkg", "6.2.1");
        Assert.Equal(NuGetFramework.Parse("net10.0"), f.ChosenGroupTfm);
        Assert.Equal("[17.0.0, 18.0.0)", Assert.Single(f.Umbraco).Range.ToNormalizedString());

        var at13 = await new FactsCollector(feed).CollectAsync("Pkg", NuGetVersion.Parse("6.2.1"), Target("13.5.0", "net8.0"), Options, Ct);
        Assert.Equal("[13.0.0, 14.0.0)", Assert.Single(at13.Umbraco).Range.ToNormalizedString());
    }

    [Fact]
    public async Task UnlistedLowestVersion_IsSkippedInTheWalk()
    {
        var feed = new FakeFeed()
            .Add("A", "1.0.0", "B [1.0.0, )")
            .Add("B", "1.0.0", [("net10.0", ["Newtonsoft.Json [13.0.1, )"])], listed: false)
            .Add("B", "1.1.0", "Umbraco.Cms.Core [17.0.0, )");
        var f = await Collect(feed, "A", "1.0.0");
        Assert.Equal("B", Assert.Single(f.Umbraco).ViaPackageId);
    }

    [Fact]
    public async Task Facts_CarryVersionsDetailsAndProjects()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, )").Add("Pkg", "2.0.0-beta1", "Umbraco.Cms.Core [18.0.0, )");
        var f = await Collect(feed, "Pkg", "1.0.0");
        Assert.Equal([NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0-beta1")], f.AllVersions);
        Assert.Equal(["src/Site"], f.ProjectDirectories);
        Assert.False(f.ContentsKnown);
        Assert.False(f.IsServerOnly);
        Assert.Null(f.Marketplace);
    }

    [Fact]
    public async Task DetailsAreCachedPerIdAndVersion()
    {
        var feed = new FakeFeed()
            .Add("A", "1.0.0", "Shared [1.0.0, )", "B [1.0.0, )")
            .Add("B", "1.0.0", "Shared [1.0.0, )")
            .Add("Shared", "1.0.0", "Newtonsoft.Json [13.0.1, )");
        var collector = new FactsCollector(feed);
        await collector.CollectAsync("A", NuGetVersion.Parse("1.0.0"), Target("17.7.2"), Options, Ct);
        var first = feed.DetailCalls;
        await collector.CollectAsync("A", NuGetVersion.Parse("1.0.0"), Target("17.7.2"), Options, Ct);
        Assert.Equal(first, feed.DetailCalls);
    }

    [Fact]
    public async Task VersionNotOnFeed_ThrowsNotFound()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, )");
        var e = await Assert.ThrowsAsync<PackageSourceException>(() => Collect(feed, "Pkg", "9.9.9"));
        Assert.Equal(UnknownReason.NotFound, e.Reason);
    }

    [Fact]
    public async Task DependencySourceFailure_Propagates()
    {
        var feed = new FakeFeed().Add("A", "1.0.0", "B [1.0.0, )");
        feed.FailingIds.Add("B");
        var e = await Assert.ThrowsAsync<PackageSourceException>(() => Collect(feed, "A", "1.0.0"));
        Assert.Equal(UnknownReason.SourceError, e.Reason);
    }

    [Fact]
    public async Task MissingDependencyId_IsNotAPath_AndDoesNotThrow()
    {
        var feed = new FakeFeed().Add("A", "1.0.0", "Gone [1.0.0, )", "Umbraco.Cms.Core [17.0.0, )");
        var f = await Collect(feed, "A", "1.0.0");
        Assert.Single(f.Umbraco);
    }

    [Fact]
    public async Task LatestPublished_IsTheNewestVersionWithADate()
    {
        var d1 = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var d2 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var feed = new FakeFeed()
            .Add("P", "1.0.0", [("net10.0", ["Umbraco.Cms.Core [17.0.0, )"])], published: d1)
            .Add("P", "1.1.0", [("net10.0", ["Umbraco.Cms.Core [17.0.0, )"])], published: d2)
            .Add("P", "1.2.0", [("net10.0", ["Umbraco.Cms.Core [17.0.0, )"])], listed: false, published: null);
        Assert.Equal(d2, (await Collect(feed, "P", "1.0.0")).LatestPublished);
    }

    [Fact]
    public void IsServerOnly_NeedsKnownContentsWithNoClientSignals()
    {
        InspectionResult Ins(bool clientAssets = false, bool reader = false) =>
            new("P", "1.0.0", false, null, false, reader, false, clientAssets, ["net10.0"], "hash", []);
        PackageFacts Facts(InspectionResult? own, params InspectionResult[] deps) => new(
            "P", NuGetVersion.Parse("1.0.0"), null!, null, [], [], [], own, deps, null, [], []);

        Assert.False(Facts(null).IsServerOnly);
        Assert.True(Facts(Ins()).IsServerOnly);
        Assert.False(Facts(Ins(clientAssets: true)).IsServerOnly);
        Assert.False(Facts(Ins(), Ins(clientAssets: true)).IsServerOnly);
        Assert.False(Facts(Ins(reader: true)).IsServerOnly); // R31: H09 is never discarded for server-only status
    }
}

public class FactsCollectorMarketplaceTests
{
    private sealed class FakeMarketplace(MarketplaceInfo? info) : IMarketplaceSource
    {
        public List<(string Id, NuGetVersion Target)> Calls { get; } = [];

        public Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct)
        {
            Calls.Add((id, target));
            return Task.FromResult(info);
        }
    }

    [Fact]
    public async Task Collector_FillsMarketplace_QueryingTheReleaseBase()
    {
        var info = new MarketplaceInfo("u", false, null, true, null, "Pkg");
        var mp = new FakeMarketplace(info);
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [18.0.0, )");
        var f = await new FactsCollector(feed, mp).CollectAsync("Pkg", NuGetVersion.Parse("1.0.0"),
            TestTargets.Target("18.0.0-rc2"), new FactsOptions(false, []), CancellationToken.None);
        Assert.Same(info, f.Marketplace);
        Assert.Equal(("Pkg", NuGetVersion.Parse("18.0.0")), Assert.Single(mp.Calls));
    }
}
