using PackageCheck.Core.Heuristics;
using static PackageCheck.Core.Tests.Heuristics.HeuristicFacts;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Heuristics;

public class H01Tests
{
    [Fact]
    public void H01_Bounded_IsMedium()
    {
        var ev = new H01RangeAllows().Evaluate(Pkg("Umbraco.Community.Contentment", "6.2.1", "[17.0.0, 18.0.0)"), Target("17.7.2")).Single();
        Assert.Equal(("H01", EvidenceResult.Positive, EvidenceWeight.Medium), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("Umbraco.Cms.* [17.0.0, 18.0.0) allows 17.7.2", ev.Detail);
    }

    [Fact]
    public void H01_Open_IsLow() =>
        Assert.Equal(EvidenceWeight.Low, new H01RangeAllows().Evaluate(Pkg("astroboard", "1.1.3", "13.8.1"), Target("17.7.2")).Single().Weight);

    [Fact]
    public void H01_OneOpenAmongBounded_IsLow() =>
        Assert.Equal(EvidenceWeight.Low,
            new H01RangeAllows().Evaluate(Pkg("P", "1.0.0", "[17.0.0, 18.0.0)", "17.0.0"), Target("17.7.2")).Single().Weight);

    [Fact]
    public void H01_Excluded_EmitsNothing() =>
        Assert.Empty(new H01RangeAllows().Evaluate(Pkg("P", "1.0.0", "[18.0.0, 19.0.0)"), Target("17.7.2")));

    [Fact]
    public void H01_NoPath_EmitsNothing() =>
        Assert.Empty(new H01RangeAllows().Evaluate(Pkg("P", "1.0.0"), Target("17.7.2")));

    [Fact]
    public void H01_PrereleaseTarget_ComparesReleaseBase()
    {
        // Plain NuGet would say 18.0.0-rc2 < 18.0.0 and exclude it (design 6.1, Pascal 2026-10-30).
        var ev = new H01RangeAllows().Evaluate(Pkg("P", "1.0.0", "[18.0.0, )"), Target("18.0.0-rc2")).Single();
        Assert.Equal(EvidenceResult.Positive, ev.Result);
    }

    [Fact]
    public void H01_TransitiveRange_NamesTheVia()
    {
        var f = Build("A", "1.0.0", [new("jcdcdev.Umbraco.Core", "Umbraco.Cms.Core", NuGet.Versioning.VersionRange.Parse("[17.0.0, 18.0.0)"), 2)]);
        Assert.EndsWith("(H04 via jcdcdev.Umbraco.Core)", new H01RangeAllows().Evaluate(f, Target("17.7.2")).Single().Detail);
    }
}

public class H02Tests
{
    [Fact]
    public void H02_Contentment722_ExcludesTarget1772()
    {
        // Contentment 7.2.2's net10.0 group declares [18.0.0, 19.0.0) (docs/spikes.md S2). 6.2.1 allows 17.7.2 (ruling R19).
        var ev = new H02UpperBoundExcludes().Evaluate(Pkg("Umbraco.Community.Contentment", "7.2.2", "[18.0.0, 19.0.0)"), Target("17.7.2")).Single();
        Assert.Equal(("H02", EvidenceResult.Negative, EvidenceWeight.High), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("Umbraco.Cms.Core [18.0.0, 19.0.0) excludes 17.7.2", ev.Detail);
    }

    [Fact]
    public void H02_UpperBoundBelowTarget_Excludes() =>
        Assert.Single(new H02UpperBoundExcludes().Evaluate(Pkg("P", "6.2.1", "[16.0.0, 17.0.0)"), Target("17.7.2")));

    [Fact]
    public void H02_Allows_EmitsNothing() =>
        Assert.Empty(new H02UpperBoundExcludes().Evaluate(Pkg("P", "6.2.1", "[17.0.0, 18.0.0)"), Target("17.7.2")));

    [Fact]
    public void H02_EachExcludingRange_IsOneLine() =>
        Assert.Equal(2, new H02UpperBoundExcludes().Evaluate(Pkg("P", "1", "[16.0.0, 17.0.0)", "[16.0.0, 17.0.0)"), Target("17.7.2")).Count());

    [Fact]
    public void H02_PrereleaseTarget_UpperBoundAtBase_Excludes()
    {
        var ev = new H02UpperBoundExcludes().Evaluate(Pkg("P", "6.3.3", "[17.0.0, 18.0.0)"), Target("18.0.0-rc2")).Single();
        Assert.Equal(("H02", EvidenceResult.Negative, EvidenceWeight.High), (ev.Check, ev.Result, ev.Weight));
    }
}

public class H03Tests
{
    [Fact]
    public void H03_Astroboard_OpenRangeFrom13_8_1()
    {
        var ev = new H03OpenRangeFromOldMajor().Evaluate(Pkg("astroboard", "1.1.3", "13.8.1"), Target("17.7.2", "net10.0")).Single();
        Assert.Equal(("H03", EvidenceResult.Negative, EvidenceWeight.Low), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("open range >= 13.8.1", ev.Detail);
    }

    [Fact]
    public void H03_SkippedForTarget13() => Assert.False(new H03OpenRangeFromOldMajor().AppliesTo(Target("13.9.0", "net8.0")));

    [Fact]
    public void H03_AppliesFrom14() => Assert.True(new H03OpenRangeFromOldMajor().AppliesTo(Target("14.0.0", "net8.0")));

    [Fact]
    public void H03_OpenFrom14_EmitsNothing() =>
        Assert.Empty(new H03OpenRangeFromOldMajor().Evaluate(Pkg("P", "1", "14.0.0"), Target("17.7.2")));

    [Fact]
    public void H03_Bounded_EmitsNothing() =>
        Assert.Empty(new H03OpenRangeFromOldMajor().Evaluate(Pkg("P", "1", "[13.0.0, 18.0.0)"), Target("17.7.2")));

    [Fact]
    public void H03_RangeExcludes_EmitsNothing() =>
        Assert.Empty(new H03OpenRangeFromOldMajor().Evaluate(Pkg("P", "1", "13.0.0", "[13.0.0, 14.0.0)"), Target("17.7.2")));

    [Fact]
    public void H03_NoFloor_CountsAsBelow14()
    {
        // Ruling R28: a missing floor counts as below 14.
        var ev = new H03OpenRangeFromOldMajor().Evaluate(Pkg("P", "1", "(, )"), Target("17.7.2")).Single();
        Assert.Equal("open range >= (no floor)", ev.Detail);
    }
}
