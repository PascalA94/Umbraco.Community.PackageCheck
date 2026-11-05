using Microsoft.Extensions.Time.Testing;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Verdicts;
using PackageCheck.Inspect;
using PackageCheck.Tests.Support;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests;

/// <summary>The real inspector over synthetic nupkgs, through FactsCollector and the VerdictEngine.</summary>
public class FactsCollectorInspectionTests
{
    private static readonly ScanOptions Inspect = new(Inspect: true, AllowPrerelease: false, Verbose: false);
    private static readonly ScanOptions NoInspect = new(Inspect: false, AllowPrerelease: false, Verbose: false);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static VerdictEngine Engine(FakeFeed feed) => new(
        new FactsCollector(feed, inspector: new NupkgInspector(ArchiveLimits.Default)),
        OverrideSet.Empty, new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)));

    private static SitePackage Site(string id, string version) =>
        new(new PackageRef(id, version, NuGetVersion.Parse(version), Direct: true, UnresolvedReason: null), ["Site"]);

    private static Task<PackageVerdict> Judge(FakeFeed feed, string id, string version, ScanOptions o, string target = "17.7.2") =>
        Engine(feed).EvaluateAsync(Site(id, version), Target(target), o, Ct);

    private static string Dll() => NupkgBuilderFile("lib/net10.0/Pkg.dll", [0x4D, 0x5A]);

    private static string NupkgBuilderFile(string path, byte[] content) => NupkgBuilder.Create("Pkg", "1.0.0").WithFile(path, content).Build();

    [Fact]
    public async Task NoInspect_NoNupkgOpened()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)").WithNupkg("Pkg", "1.0.0", Dll());
        await Judge(feed, "Pkg", "1.0.0", NoInspect);
        Assert.Empty(feed.OpenedNupkgs);
    }

    [Fact]
    public async Task OverCap_FallsBackToMetadataOnly()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)");
        feed.TooLarge.Add("Pkg/1.0.0");
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.Medium), (v.Verdict, v.Confidence)); // row 7 from metadata
        Assert.Contains(v.Evidence, e => e.Detail == "Pkg 1.0.0: nupkg over size cap; metadata only");
    }

    [Fact]
    public async Task UnsafeArchive_IsUnknownUnsafeArchive()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "1.0.0", NupkgBuilder.Create("Pkg", "1.0.0").WithRawEntryName("../evil").Build());
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.UnsafeArchive), (v.Verdict, v.Reason));
    }

    [Fact]
    public async Task TempNupkg_IsDeletedOnFailure()
    {
        var path = NupkgBuilder.Create("Pkg", "1.0.0").WithRawEntryName("/rooted").Build();
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)").WithNupkg("Pkg", "1.0.0", path, temporary: true);
        await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task GlobalFolderNupkg_IsKept()
    {
        var path = Dll();
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)").WithNupkg("Pkg", "1.0.0", path);
        await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ServerOnly_RealPipeline_OkNotSuspicious()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)").WithNupkg("Pkg", "1.0.0", Dll());
        var collector = new FactsCollector(feed, inspector: new NupkgInspector(ArchiveLimits.Default));
        var f = await collector.CollectAsync("Pkg", NuGetVersion.Parse("1.0.0"), Target("17.7.2"), new FactsOptions(true, []), Ct);
        Assert.True(f.ContentsKnown);
        Assert.True(f.IsServerOnly);
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.Medium), (v.Verdict, v.Confidence));
    }

    [Fact]
    public async Task DependencyWithClientAssets_NotServerOnly()
    {
        var feed = new FakeFeed()
            .Add("Pkg", "1.0.0", "Pkg.Core [1.0.0, )")
            .Add("Pkg.Core", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "1.0.0", Dll())
            .WithNupkg("Pkg.Core", "1.0.0", NupkgBuilder.Create("Pkg.Core", "1.0.0").WithFile("staticwebassets/App_Plugins/Pkg/a.js", "x").Build());
        var f = await new FactsCollector(feed, inspector: new NupkgInspector(ArchiveLimits.Default))
            .CollectAsync("Pkg", NuGetVersion.Parse("1.0.0"), Target("17.7.2"), new FactsOptions(true, []), Ct);
        Assert.Single(f.Dependencies);
        Assert.False(f.IsServerOnly);
    }

    [Fact]
    public async Task AssetOnlyDependency_LegacyManifest_IsOwnH08() // ruling R29
    {
        var feed = new FakeFeed()
            .Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)", "Pkg.Backoffice [1.0.0, )")
            .Add("Pkg.Backoffice", "1.0.0")
            .WithNupkg("Pkg", "1.0.0", Dll())
            .WithNupkg("Pkg.Backoffice", "1.0.0", NupkgBuilder.Create("Pkg.Backoffice", "1.0.0")
                .WithFile("content/App_Plugins/Pkg/package.manifest", "{}").WithFile("content/App_Plugins/Pkg/a.js", "x").Build());
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Contains(v.Evidence, e => e.Check == "H08");
        Assert.Equal(Verdict.Suspicious, v.Verdict);
    }

    [Fact]
    public async Task NoInspect_AstroboardShape_IsRow9() // ruling R26: H03 + H06 failing from the nuspec group
    {
        var feed = new FakeFeed().Add("Astro", "1.1.3", [("net8.0", ["Umbraco.Cms.Web.Website 13.8.1"])]);
        var v = await Judge(feed, "Astro", "1.1.3", NoInspect);
        Assert.Equal((Verdict.Suspicious, (Confidence?)Confidence.Low), (v.Verdict, v.Confidence));
        Assert.Contains(v.Evidence, e => e.Check == "H03");
        Assert.Contains(v.Evidence, e => e.Check == "H06" && e.Result == EvidenceResult.Negative);
        Assert.Empty(feed.OpenedNupkgs);
    }

    [Fact]
    public async Task NoInspect_AstroboardShape_Net10Group_IsOkLow() // R26's twin
    {
        var feed = new FakeFeed().Add("Astro", "1.1.3", [("net10.0", ["Umbraco.Cms.Web.Website 13.8.1"])]);
        var v = await Judge(feed, "Astro", "1.1.3", NoInspect);
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.Low), (v.Verdict, v.Confidence));
    }

    [Fact]
    public async Task H08_ContentAppPluginsManifest_Fires()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "1.0.0", NupkgBuilder.Create("Pkg", "1.0.0").WithFile("content/App_Plugins/X/package.manifest", "{}").Build());
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Contains(v.Evidence, e => e.Check == "H08" && e.IsHighNegative);
        Assert.Equal(Verdict.Suspicious, v.Verdict);
    }

    [Fact]
    public async Task OwnLegacy_DependencyRegistration_IsConflicting() // ruling R23, row 2
    {
        var feed = new FakeFeed()
            .Add("Pkg", "1.0.0", "Pkg.Ui [1.0.0, )")
            .Add("Pkg.Ui", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "1.0.0", NupkgBuilder.Create("Pkg", "1.0.0").WithFile("App_Plugins/Pkg/package.manifest", "{}").Build())
            .WithNupkg("Pkg.Ui", "1.0.0", NupkgBuilder.Create("Pkg.Ui", "1.0.0").WithFile("staticwebassets/umbraco-package.json", "{}").Build());
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.ConflictingEvidence), (v.Verdict, v.Reason));
    }

    [Fact]
    public async Task UmbracoPackageJson_LiftsToOkHigh()
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "1.0.0", NupkgBuilder.Create("Pkg", "1.0.0").WithFile("staticwebassets/umbraco-package.json", "{}").Build());
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.High), (v.Verdict, v.Confidence));
    }

    [Fact]
    public async Task UpgradeCandidate_WithLegacyManifest_IsNotRecommended()
    {
        // Row 1 at 1.0.0. Major 2 is the lowest that clears on metadata; its highest version (2.1.0) is legacy-only on
        // inspection, so the walk tries the next version in that major (design 6.1, UpgradeSelector.MaxInspections).
        var feed = new FakeFeed()
            .Add("Pkg", "1.0.0", "Umbraco.Cms.Core [16.0.0, 17.0.0)")
            .Add("Pkg", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .Add("Pkg", "2.1.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "2.1.0", NupkgBuilder.Create("Pkg", "2.1.0").WithFile("App_Plugins/Pkg/package.manifest", "{}").Build())
            .WithNupkg("Pkg", "2.0.0", NupkgBuilder.Create("Pkg", "2.0.0").WithFile("staticwebassets/umbraco-package.json", "{}").Build());
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Upgrade, "2.0.0"), (v.Verdict, v.Recommended));
    }

    // Review M3 (Q2): with --offline, or a candidate over the size cap, the recommended version was never inspected.
    // The upgrade stands on metadata, but it says so and drops to medium confidence.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninspectedCandidate_IsFlagged(bool tooLarge)
    {
        var feed = new FakeFeed()
            .Add("Pkg", "1.0.0", "Umbraco.Cms.Core [16.0.0, 17.0.0)")
            .Add("Pkg", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "1.0.0", NupkgBuilder.Create("Pkg", "1.0.0").Build());
        if (tooLarge) feed.TooLarge.Add("Pkg/2.0.0");
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Upgrade, "2.0.0", (Confidence?)Confidence.Medium), (v.Verdict, v.Recommended, v.Confidence));
        Assert.Contains(v.Evidence, e => e.Check == "inspect" && e.Detail.StartsWith("recommended version 2.0.0 was not inspected"));
    }

    [Fact]
    public async Task InspectedCandidate_StaysHigh()
    {
        var feed = new FakeFeed()
            .Add("Pkg", "1.0.0", "Umbraco.Cms.Core [16.0.0, 17.0.0)")
            .Add("Pkg", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .WithNupkg("Pkg", "2.0.0", NupkgBuilder.Create("Pkg", "2.0.0").Build());
        var v = await Judge(feed, "Pkg", "1.0.0", Inspect);
        Assert.Equal((Verdict.Upgrade, (Confidence?)Confidence.High), (v.Verdict, v.Confidence));
    }
}

