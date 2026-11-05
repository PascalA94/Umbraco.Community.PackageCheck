using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Targets;
using static PackageCheck.Core.Tests.Heuristics.HeuristicFacts;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Heuristics;

public class H05Tests
{
    [Fact]
    public void H05_RemovedPackages_ListIsPinned() =>
        Assert.Equal(["Umbraco.Cms.Web.BackOffice"], UmbracoFacts.RemovedBackofficePackages);

    [Fact]
    public void H05_DependsOnWebBackOffice_IsHighNegative()
    {
        var f = Build("astroboard", "1.1.3",
        [
            new("astroboard", "Umbraco.Cms.Web.BackOffice", VersionRange.Parse("13.8.1"), 1),
            new("astroboard", "Umbraco.Cms.Web.Website", VersionRange.Parse("13.8.1"), 1),
        ]);
        var ev = new H05RemovedCorePackage().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal(("H05", EvidenceResult.Negative, EvidenceWeight.High), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("depends on Umbraco.Cms.Web.BackOffice, removed in the new backoffice", ev.Detail);
    }

    [Fact]
    public void H05_CaseInsensitive() =>
        Assert.Single(new H05RemovedCorePackage().Evaluate(
            Build("P", "1", [new("P", "umbraco.cms.web.backoffice", VersionRange.Parse("13.0.0"), 1)]), Target("17.7.2")));

    [Fact]
    public void H05_WithoutRemovedPackage_EmitsNothing() =>
        Assert.Empty(new H05RemovedCorePackage().Evaluate(Pkg("P", "1", "[17.0.0, 18.0.0)"), Target("17.7.2")));

    [Fact]
    public void H05_SkippedForTarget13() => Assert.False(new H05RemovedCorePackage().AppliesTo(Target("13.9.0", "net8.0")));
}

public class H14Tests
{
    [Fact]
    public void H14_DeprecatedWithAlternate_IsHighNegative_AndCarriesTheAlternate()
    {
        var f = Build("Old.Pkg", "1.0.0", [], deprecation: new DeprecationInfo(["Legacy"], "New.Pkg", null));
        var ev = new H14NuGetDeprecation().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal(("H14", EvidenceResult.Negative, EvidenceWeight.High), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("New.Pkg", ev.Alternate);
        Assert.Contains("New.Pkg", ev.Detail);
    }

    [Fact]
    public void H14_DeprecatedWithoutAlternate_IsInfo()
    {
        var f = Build("Old.Pkg", "1.0.0", [], deprecation: new DeprecationInfo(["CriticalBugs"], null, null));
        var ev = new H14NuGetDeprecation().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal((EvidenceResult.Info, EvidenceWeight.Info, (string?)null), (ev.Result, ev.Weight, ev.Alternate));
    }

    [Fact]
    public void H14_NotDeprecated_EmitsNothing() =>
        Assert.Empty(new H14NuGetDeprecation().Evaluate(Pkg("P", "1"), Target("17.7.2")));
}

public class H15Tests
{
    [Fact]
    public void H15_OnlyAPrereleaseClears_IsInfo_NamingTheLowest()
    {
        var f = Build("Skybrud.Umbraco.Redirects", "16.0.0", [],
            allVersions: ["16.0.0", "17.0.0", "17.1.0-beta002", "17.1.0-beta003"], clearing: ["17.1.0-beta002", "17.1.0-beta003"]);
        var ev = new H15PrereleaseOnly().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal(("H15", EvidenceResult.Info, EvidenceWeight.Info), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("only a prerelease supports the target: 17.1.0-beta002", ev.Detail);
    }

    [Fact]
    public void H15_AStableClears_EmitsNothing() =>
        Assert.Empty(new H15PrereleaseOnly().Evaluate(
            Build("P", "1.0.0", [], allVersions: ["1.0.0", "2.0.0", "3.0.0-beta1"], clearing: ["2.0.0", "3.0.0-beta1"]), Target("17.7.2")));

    [Fact]
    public void H15_NothingClears_EmitsNothing() =>
        Assert.Empty(new H15PrereleaseOnly().Evaluate(Build("P", "1.0.0", [], allVersions: ["1.0.0"], clearing: []), Target("17.7.2")));

    [Fact]
    public void H15_ClearingNotComputed_EmitsNothing() =>
        Assert.Empty(new H15PrereleaseOnly().Evaluate(Build("P", "1.0.0", [], allVersions: ["1.0.0", "2.0.0-beta"]), Target("17.7.2")));

    [Fact]
    public void H15_OlderPrereleases_DoNotCount() =>
        Assert.Empty(new H15PrereleaseOnly().Evaluate(
            Build("P", "2.0.0", [], allVersions: ["1.0.0-beta", "2.0.0"], clearing: ["1.0.0-beta"]), Target("17.7.2")));
}

public class H18Tests
{
    private static readonly UmbracoVersionsTable Table = new(
    [
        new UmbracoMajor(17, true, new DateOnly(2028, 11, 27), "net10.0", new DateOnly(2025, 11, 27)),
        new UmbracoMajor(18, false, new DateOnly(2027, 6, 25), "net10.0", null),
    ]);

    [Fact]
    public void H18_LastReleaseBeforeTargetMajor_IsInfo()
    {
        var f = Build("astroboard", "1.1.3", [], latestPublished: new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var ev = new H18StaleRelease(Table).Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal(("H18", EvidenceResult.Info, EvidenceWeight.Info), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("last release 2024-03-01 is older than Umbraco 17 (released 2025-11-27)", ev.Detail);
    }

    [Fact]
    public void H18_RecentRelease_EmitsNothing() =>
        Assert.Empty(new H18StaleRelease(Table).Evaluate(
            Build("P", "1", [], latestPublished: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), Target("17.7.2")));

    [Fact]
    public void H18_UnknownDates_EmitNothing()
    {
        Assert.Empty(new H18StaleRelease(Table).Evaluate(Build("P", "1", []), Target("17.7.2")));
        Assert.Empty(new H18StaleRelease(Table).Evaluate(
            Build("P", "1", [], latestPublished: DateTimeOffset.UnixEpoch), Target("18.0.0")));
        Assert.Empty(new H18StaleRelease(Table).Evaluate(
            Build("P", "1", [], latestPublished: DateTimeOffset.UnixEpoch), Target("19.0.0")));
    }
}
