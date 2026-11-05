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
