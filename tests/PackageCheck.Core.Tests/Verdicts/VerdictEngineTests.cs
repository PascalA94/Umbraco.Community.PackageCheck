using Microsoft.Extensions.Time.Testing;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Core.Verdicts;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Verdicts;

public class VerdictEngineTests
{
    private static readonly ScanOptions Metadata = new(Inspect: false, AllowPrerelease: false, Verbose: false);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static VerdictEngine Engine(IPackageMetadataSource feed) =>
        new(new FactsCollector(feed), OverrideSet.Empty, new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)));

    private static SitePackage Site(string id, string version) =>
        new(new PackageRef(id, version, NuGetVersion.Parse(version), Direct: true, UnresolvedReason: null), ["Site"]);

    private static Task<PackageVerdict> Judge(IPackageMetadataSource feed, string id, string version, string target, ScanOptions? o = null) =>
        Engine(feed).EvaluateAsync(Site(id, version), Target(target), o ?? Metadata, Ct);

    // Ranges from docs/spikes.md (S2) and the M0 recordings; 6.1.x really declares [17.0.0, 19.0.0) (ruling R25 caveat).
    private static FakeFeed Contentment() => new FakeFeed()
        .Add("Umbraco.Community.Contentment", "5.2.1", "Umbraco.Cms.Core [15.0.0, 16.0.0)")
        .Add("Umbraco.Community.Contentment", "6.1.4", "Umbraco.Cms.Core [17.0.0, 19.0.0)")
        .Add("Umbraco.Community.Contentment", "6.2.1", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
        .Add("Umbraco.Community.Contentment", "6.3.4", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
        .Add("Umbraco.Community.Contentment", "7.2.1", "Umbraco.Cms.Core [18.0.0, 19.0.0)")
        .Add("Umbraco.Community.Contentment", "7.2.2", "Umbraco.Cms.Core [18.0.0, 19.0.0)");

    [Fact]
    public async Task Contentment621_Target1772_IsOk() // ruling R19: its net10.0 range allows 17.7.2
    {
        var v = await Judge(Contentment(), "Umbraco.Community.Contentment", "6.2.1", "17.7.2");
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.Medium), (v.Verdict, v.Confidence));
    }

    [Fact]
    public async Task Contentment521_Target1772_UpgradeTo634()
    {
        var v = await Judge(Contentment(), "Umbraco.Community.Contentment", "5.2.1", "17.7.2");
        Assert.Equal((Verdict.Upgrade, (Confidence?)Confidence.High, "6.3.4"), (v.Verdict, v.Confidence, v.Recommended));
        Assert.Null(v.HighestCompatible);
        Assert.Equal(("7.2.2", false), (v.LatestStable, v.LatestStableClears)); // shown as "does not support"
    }

    [Fact]
    public async Task Contentment521_Target1830_PicksLowestClearingMajor()
    {
        var v = await Judge(Contentment(), "Umbraco.Community.Contentment", "5.2.1", "18.3.0");
        Assert.Equal((Verdict.Upgrade, "6.1.4", "7.2.2"), (v.Verdict, v.Recommended, v.HighestCompatible));
    }

    [Fact]
    public async Task Contentment721_Target1772_IsNoRelease()
    {
        var v = await Judge(Contentment(), "Umbraco.Community.Contentment", "7.2.1", "17.7.2");
        Assert.Equal((Verdict.NoRelease, (string?)null), (v.Verdict, v.Recommended));
    }

    private static FakeFeed SkybrudImport() => new FakeFeed()
        .Add("Skybrud.Umbraco.Redirects.Import", "13.0.1", "Umbraco.Cms.Core [13.0.0, 14.0.0)")
        .Add("Skybrud.Umbraco.Redirects.Import", "17.0.0-beta001", "Umbraco.Cms.Core [17.0.0, 18.0.0)");

    [Fact]
    public async Task Skybrud_OnlyBetaClears_NoReleaseMentionsPrerelease()
    {
        var v = await Judge(SkybrudImport(), "Skybrud.Umbraco.Redirects.Import", "13.0.1", "17.7.2");
        Assert.Equal((Verdict.NoRelease, "17.0.0-beta001"), (v.Verdict, v.PrereleaseCompatible));
        Assert.Contains(v.Evidence, e => e is { Check: "H15", Detail: "only a prerelease supports the target: 17.0.0-beta001" });
    }

    [Fact]
    public async Task Skybrud_WithPrerelease_UpgradeToBeta()
    {
        var v = await Judge(SkybrudImport(), "Skybrud.Umbraco.Redirects.Import", "13.0.1", "17.7.2", Metadata with { AllowPrerelease = true });
        Assert.Equal((Verdict.Upgrade, "17.0.0-beta001", (string?)null), (v.Verdict, v.Recommended, v.PrereleaseCompatible));
    }

    [Fact]
    public async Task UnlistedVersion_IsNeverRecommended()
    {
        var feed = new FakeFeed()
            .Add("P", "1.0.0", "Umbraco.Cms.Core [13.0.0, 14.0.0)")
            .Add("P", "2.0.0", [("net10.0", ["Umbraco.Cms.Core [17.0.0, 18.0.0)"])], listed: false);
        Assert.Equal(Verdict.NoRelease, (await Judge(feed, "P", "1.0.0", "17.7.2")).Verdict);
    }

    [Fact]
    public async Task PrereleaseTarget_UsesReleaseBase_EndToEnd()
    {
        var feed = new FakeFeed().Add("P", "1.0.0", "Umbraco.Cms.Core [18.0.0, )");
        var v = await Judge(feed, "P", "1.0.0", "18.0.0-rc2");
        Assert.Equal(Verdict.Ok, v.Verdict);
    }

    [Fact]
    public async Task DeprecatedWithAlternate_IsReplacedNamingAlternate()
    {
        // Plumber.Workflow's shape: open range from 10, deprecated on NuGet with Umbraco.Workflow as the alternate.
        var feed = new FakeFeed().Add("Plumber.Workflow", "10.1.2", [("net6.0", ["Umbraco.Cms.Web.Website [10.0.0, )"])],
            deprecation: new DeprecationInfo(["Legacy"], "Umbraco.Workflow", null));
        var v = await Judge(feed, "Plumber.Workflow", "10.1.2", "17.7.2");
        Assert.Equal((Verdict.Replaced, (Confidence?)Confidence.High, "Umbraco.Workflow"), (v.Verdict, v.Confidence, v.Recommended));
    }

    [Fact]
    public async Task H14WithRow7_AddsNoteOnly()
    {
        var feed = new FakeFeed().Add("P", "1.0.0", [("net10.0", ["Umbraco.Cms.Core [17.0.0, 18.0.0)"])],
            deprecation: new DeprecationInfo(["Legacy"], "New.P", null));
        var v = await Judge(feed, "P", "1.0.0", "17.7.2");
        Assert.Equal(Verdict.Ok, v.Verdict);
        Assert.Contains(v.Evidence, e => e.Check == "H14");
    }

    [Fact]
    public async Task H14WithRow3_IsReplacedHigh()
    {
        var feed = new FakeFeed().Add("P", "1.0.0", [("net10.0", ["Umbraco.Cms.Core [17.0.0, 18.0.0)", "Umbraco.Cms.Web.BackOffice [17.0.0, 18.0.0)"])],
            deprecation: new DeprecationInfo(["Legacy"], "New.P", null));
        var v = await Judge(feed, "P", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.Replaced, (Confidence?)Confidence.High, "New.P"), (v.Verdict, v.Confidence, v.Recommended));
    }

    [Fact]
    public async Task H14Row1_UpgradeToSameIdNotDeprecated_StaysUpgrade() // ruling R27
    {
        var feed = new FakeFeed()
            .Add("P", "1.0.0", [("net10.0", ["Umbraco.Cms.Core [16.0.0, 17.0.0)"])], deprecation: new DeprecationInfo(["Legacy"], "New.P", null))
            .Add("P", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)");
        var v = await Judge(feed, "P", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.Upgrade, "2.0.0"), (v.Verdict, v.Recommended));
    }

    [Fact]
    public async Task H14Row1_NoRelease_BecomesReplaced_KeepingRangeEvidence() // ruling R27
    {
        var feed = new FakeFeed().Add("P", "1.0.0", [("net10.0", ["Umbraco.Cms.Core [16.0.0, 17.0.0)"])],
            deprecation: new DeprecationInfo(["Legacy"], "New.P", null));
        var v = await Judge(feed, "P", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.Replaced, "New.P"), (v.Verdict, v.Recommended));
        Assert.Contains(v.Evidence, e => e.Check == "H02");
    }

    [Fact]
    public async Task NoUmbracoPath_IsNotUmbraco()
    {
        var feed = new FakeFeed().Add("Newtonsoft.Json", "13.0.3", [("net6.0", Array.Empty<string>())]);
        var v = await Judge(feed, "Newtonsoft.Json", "13.0.3", "17.7.2");
        Assert.Equal((Verdict.NotUmbraco, (Confidence?)null), (v.Verdict, v.Confidence));
    }

    [Fact]
    public async Task SourceError_IsUnknownSourceError()
    {
        var feed = new FakeFeed().Add("P", "1.0.0", "Umbraco.Cms.Core [17.0.0, )");
        feed.FailingIds.Add("P");
        var v = await Judge(feed, "P", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.SourceError), (v.Verdict, v.Reason));
        Assert.Single(v.Evidence);
    }

    [Fact]
    public async Task CandidateSourceFailure_IsUnknownSourceError_NotACrash() // M2 review I1
    {
        var feed = new FakeFeed()
            .Add("P", "1.0.0", "Umbraco.Cms.Core [16.0.0, 17.0.0)")
            .Add("P", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            // The newest version has a date, so the root collection never touches 2.0.0; only the walk does.
            .Add("P", "3.0.0", [("net10.0", ["Umbraco.Cms.Core [18.0.0, 19.0.0)"])], published: DateTimeOffset.UnixEpoch);
        feed.FailingVersions.Add("P/2.0.0");
        var v = await Judge(feed, "P", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.SourceError), (v.Verdict, v.Reason));
    }

    [Fact]
    public async Task DeprecatedNonUmbracoPackage_StaysNotUmbraco() // M2 review I2
    {
        var feed = new FakeFeed().Add("Old.Lib", "1.0.0", [("net6.0", Array.Empty<string>())],
            deprecation: new DeprecationInfo(["Legacy"], "New.Lib", null));
        var v = await Judge(feed, "Old.Lib", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.NotUmbraco, (string?)null), (v.Verdict, v.Recommended));
    }

    [Fact]
    public async Task UnresolvableDependency_WithNoPath_IsUnknownNotFound() // M2 review I3a
    {
        var feed = new FakeFeed().Add("A", "1.0.0", "Private.Only [1.0.0, )");
        var v = await Judge(feed, "A", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.NotFound), (v.Verdict, v.Reason));
        Assert.Contains(v.Evidence, e => e.Detail.Contains("Private.Only"));
    }

    [Fact]
    public async Task NoCompatibleDependencyGroup_IsNotNotUmbraco() // M2 review I3b
    {
        var feed = new FakeFeed()
            .Add("P", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .Add("P", "1.0.0", [("net8.0", ["Umbraco.Cms.Core [13.0.0, 14.0.0)"])]);
        var v = await Engine(feed).EvaluateAsync(Site("P", "2.0.0"), Target("13.9.0", "net8.0"), Metadata, Ct);
        Assert.Equal(Verdict.NoRelease, v.Verdict);
        Assert.Contains(v.Evidence, e => e is { Check: "H02" } && e.Detail.Contains("net8.0"));
    }

    [Fact]
    public async Task NotFound_IsUnknownNotFound()
    {
        var v = await Judge(new FakeFeed(), "Gone", "1.0.0", "17.7.2");
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.NotFound), (v.Verdict, v.Reason));
    }

    [Fact]
    public async Task UnresolvedRef_IsUnknownUnresolvedProperty_NoHttp()
    {
        var feed = new CountingFeed();
        var pkg = new SitePackage(new PackageRef("P", "$(PVersion)", null, true, "unresolved-property"), ["Site"]);
        var v = await Engine(feed).EvaluateAsync(pkg, Target("17.7.2"), Metadata, Ct);
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.UnresolvedProperty), (v.Verdict, v.Reason));
        Assert.Contains("$(PVersion)", Assert.Single(v.Evidence).Detail);
        Assert.Equal(0, feed.Calls);
    }

    [Fact]
    public async Task Verdict_CarriesPackageAndMarketplace()
    {
        var pkg = Site("Umbraco.Community.Contentment", "6.2.1");
        var v = await Engine(Contentment()).EvaluateAsync(pkg, Target("17.7.2"), Metadata, Ct);
        Assert.Same(pkg, v.Package);
        Assert.Null(v.Override);
    }

    private sealed class CountingFeed : IPackageMetadataSource
    {
        public int Calls { get; private set; }
        public Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct) { Calls++; throw new InvalidOperationException(); }
        public Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct) { Calls++; throw new InvalidOperationException(); }
        public Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct) { Calls++; throw new InvalidOperationException(); }
    }
}

public class WireValueTests
{
    [Fact]
    public void Verdicts_HaveExactWireValues() =>
        Assert.Equal(["ok", "upgrade", "no-release", "suspicious", "replaced", "unknown", "not-umbraco"],
            Enum.GetValues<Verdict>().Select(v => v.ToWireValue()));

    [Fact]
    public void Confidences_HaveExactWireValues() =>
        Assert.Equal(["high", "medium", "low"], Enum.GetValues<Confidence>().Select(c => c.ToWireValue()));
}

public class VerdictEngineOverrideTests
{
    private static readonly ScanOptions Metadata = new(false, false, false);

    private static VerdictEngine Engine(IPackageMetadataSource feed, OverrideSet set) =>
        new(new FactsCollector(feed), set, new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)));

    private static SitePackage Site(string id, string version) =>
        new(new PackageRef(id, version, NuGetVersion.Parse(version), true, null), ["Site"]);

    private static OverrideSet Broken(string id, string versions) => new([PackageCheck.Core.Tests.Overrides.OverrideApplierTests.Entry(id: id, versions: versions)], "test");

    // astroboard 1.1.3's metadata (docs/spikes.md S2): BackOffice + Website at >= 13.8.1 -> row 4 suspicious.
    private static FakeFeed Astroboard() => new FakeFeed()
        .Add("astroboard", "1.1.3", [("net8.0", ["Umbraco.Cms.Web.BackOffice [13.8.1, )", "Umbraco.Cms.Web.Website [13.8.1, )"])]);

    [Fact]
    public async Task Astroboard_SeedOverride_IsNoRelease_KeepsComputedSuspicious()
    {
        var v = await Engine(Astroboard(), Broken("astroboard", "[1.0.0, )")).EvaluateAsync(Site("astroboard", "1.1.3"), Target("17.7.2"), Metadata, TestContext.Current.CancellationToken);
        Assert.Equal((Verdict.NoRelease, Verdict.Suspicious, "override"), (v.Verdict, v.Override!.ComputedVerdict, v.Override.Label));
    }

    [Fact]
    public async Task Astroboard_WithoutOverrides_IsComputedSuspicious()
    {
        var v = await Engine(Astroboard(), OverrideSet.Empty).EvaluateAsync(Site("astroboard", "1.1.3"), Target("17.7.2"), Metadata, TestContext.Current.CancellationToken);
        Assert.Equal((Verdict.Suspicious, (Confidence?)Confidence.Medium, (OverrideApplied?)null), (v.Verdict, v.Confidence, v.Override));
    }

    [Fact]
    public async Task Broken_WithAClearingNewerVersion_IsUpgradeToIt()
    {
        var feed = Astroboard().Add("astroboard", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)");
        var v = await Engine(feed, Broken("astroboard", "[1.0.0, 2.0.0)")).EvaluateAsync(Site("astroboard", "1.1.3"), Target("17.7.2"), Metadata, TestContext.Current.CancellationToken);
        Assert.Equal((Verdict.Upgrade, "2.0.0"), (v.Verdict, v.Recommended));
    }

    [Fact]
    public async Task BrokenCandidate_IsNeverRecommended()
    {
        var feed = new FakeFeed()
            .Add("P", "1.0.0", "Umbraco.Cms.Core [16.0.0, 17.0.0)")
            .Add("P", "2.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)")
            .Add("P", "2.1.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)");
        var v = await Engine(feed, Broken("P", "[2.1.0]")).EvaluateAsync(Site("P", "1.0.0"), Target("17.7.2"), Metadata, TestContext.Current.CancellationToken);
        Assert.Equal((Verdict.Upgrade, "2.0.0", (OverrideApplied?)null), (v.Verdict, v.Recommended, v.Override));
    }

    [Fact]
    public async Task UnexpectedException_IsUnknownSourceError_WithAFixedMessage() // security review M3 (S2)
    {
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)");
        feed.CrashingIds.Add("Pkg");
        var v = await new VerdictEngine(new FactsCollector(feed), OverrideSet.Empty, new FakeTimeProvider()).EvaluateAsync(
            new SitePackage(new PackageRef("Pkg", "1.0.0", NuGetVersion.Parse("1.0.0"), true, null), ["Site"]),
            Target("17.7.2"), new ScanOptions(false, false, false), CancellationToken.None);
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.SourceError), (v.Verdict, v.Reason));
        Assert.DoesNotContain(v.Evidence, e => e.Detail.Contains("boom"));
    }
}
