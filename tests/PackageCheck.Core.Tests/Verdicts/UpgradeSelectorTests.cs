using NuGet.Versioning;
using PackageCheck.Core.Verdicts;

namespace PackageCheck.Core.Tests.Verdicts;

public class UpgradeSelectorTests
{
    private static IReadOnlyList<NuGetVersion> V(params string[] v) => v.Select(NuGetVersion.Parse).ToList();

    private static Func<NuGetVersion, Task<bool>> Clears(params string[] v)
    {
        var set = V(v).ToHashSet();
        return x => Task.FromResult(set.Contains(x));
    }

    [Fact]
    public async Task LowestClearingMajor_HighestPatchInIt()
    {
        var all = V("5.2.1", "6.1.4", "6.2.1", "6.3.4", "7.2.1", "7.2.2");
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("5.2.1"), all, Clears("6.1.4", "7.2.1", "7.2.2"), Clears("6.1.4", "7.2.1", "7.2.2"), false);
        Assert.Equal(NuGetVersion.Parse("6.1.4"), c.Recommended);
        Assert.Equal(NuGetVersion.Parse("7.2.2"), c.HighestCompatible);
        Assert.Equal(NuGetVersion.Parse("7.2.2"), c.LatestStable);
        Assert.True(c.LatestStableClears);
    }

    [Fact]
    public async Task PrefersMajorThatFitsTargetTfm()
    {
        var all = V("13.2.6", "14.2.2", "14.2.3", "17.6.0", "17.6.1", "18.3.0");
        var clears = Clears("14.2.2", "14.2.3", "17.6.0", "17.6.1");
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("13.2.6"), all, clears, clears, false, [Clears("17.6.0", "17.6.1", "18.3.0")]);
        Assert.Equal(NuGetVersion.Parse("17.6.1"), c.Recommended);
    }

    [Fact]
    public async Task EarlierPreferenceWins_ThenLaterOnes()
    {
        var all = V("13.2.6", "14.2.3", "17.6.1", "18.3.0");
        var clears = Clears("14.2.3", "17.6.1", "18.3.0");
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("13.2.6"), all, clears, clears, false,
            [Clears("18.3.0"), Clears("17.6.1", "18.3.0")]);
        Assert.Equal(NuGetVersion.Parse("18.3.0"), c.Recommended);

        var second = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("13.2.6"), all, clears, clears, false,
            [Clears(), Clears("17.6.1", "18.3.0")]);
        Assert.Equal(NuGetVersion.Parse("17.6.1"), second.Recommended);
    }

    [Fact]
    public async Task FailedPreferredVersions_AreNotInspectedTwice_PlainRuleStillRuns()
    {
        // Final review F1: 7.x is preferred but its contents fail; each version must cost one inspection, not one per
        // pass, so the plain pass still reaches 6.1.0 instead of ending in no-release.
        var all = V("5.0.0", "6.1.0", "7.2.1", "7.2.2");
        var inspected = new List<NuGetVersion>();
        Task<bool> Full(NuGetVersion v) { inspected.Add(v); return Task.FromResult(v == NuGetVersion.Parse("6.1.0")); }
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("5.0.0"), all, Clears("6.1.0", "7.2.1", "7.2.2"), Full, false,
            [Clears("7.2.1", "7.2.2"), Clears("7.2.1", "7.2.2")]);
        Assert.Equal(NuGetVersion.Parse("6.1.0"), c.Recommended);
        Assert.Equal(inspected.Count, inspected.Distinct().Count());
    }

    [Fact]
    public async Task NothingFitsTargetTfm_FallsBackToLowestClearingMajor()
    {
        var all = V("13.2.6", "14.2.3", "15.0.0");
        var clears = Clears("14.2.3", "15.0.0");
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("13.2.6"), all, clears, clears, false, [Clears(), Clears()]);
        Assert.Equal(NuGetVersion.Parse("14.2.3"), c.Recommended);
    }

    [Fact]
    public async Task HighestCompatible_IsNull_WhenItIsTheRecommendation()
    {
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("1.0.0"), V("1.0.0", "2.0.0", "3.0.0"), Clears("2.0.0"), Clears("2.0.0"), false);
        Assert.Equal((NuGetVersion.Parse("2.0.0"), (NuGetVersion?)null), (c.Recommended, c.HighestCompatible));
        Assert.Equal(NuGetVersion.Parse("3.0.0"), c.LatestStable);
        Assert.False(c.LatestStableClears);
    }

    [Fact]
    public async Task ContentsDisagree_TriesNextInMajor_CappedAtThreeInspections()
    {
        var inspected = new List<NuGetVersion>();
        Task<bool> Full(NuGetVersion v) { inspected.Add(v); return Task.FromResult(false); }
        var all = V("1.0.0", "2.0.0", "2.1.0", "2.2.0", "2.3.0", "3.0.0");
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("1.0.0"), all, _ => Task.FromResult(true), Full, false);
        Assert.Null(c.Recommended);
        Assert.Equal(V("2.3.0", "2.2.0", "2.1.0"), inspected);
    }

    [Fact]
    public async Task OnlyPrereleaseClears_WithoutFlag_IsPrereleaseCompatible()
    {
        var all = V("13.0.1", "17.0.0-beta001", "17.0.0-beta002");
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("13.0.1"), all, Clears("17.0.0-beta001", "17.0.0-beta002"), Clears("17.0.0-beta001", "17.0.0-beta002"), false);
        Assert.Null(c.Recommended);
        Assert.Equal(NuGetVersion.Parse("17.0.0-beta001"), c.PrereleaseCompatible);
    }

    [Fact]
    public async Task WithFlag_PrereleaseCanBeRecommended_AndNoPrereleaseCompatible()
    {
        var all = V("13.0.9", "17.0.5", "17.1.0-beta003", "18.0.0");
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("13.0.9"), all, Clears("17.0.5", "17.1.0-beta003"), Clears("17.0.5", "17.1.0-beta003"), true);
        Assert.Equal(NuGetVersion.Parse("17.1.0-beta003"), c.Recommended);
        Assert.Null(c.PrereleaseCompatible);
    }

    [Fact]
    public async Task OlderVersions_AreNeverCandidates()
    {
        var c = await UpgradeSelector.SelectAsync(NuGetVersion.Parse("2.0.0"), V("1.0.0", "2.0.0"), Clears("1.0.0"), Clears("1.0.0"), false);
        Assert.Null(c.Recommended);
        Assert.Null(c.HighestCompatible);
    }
}
