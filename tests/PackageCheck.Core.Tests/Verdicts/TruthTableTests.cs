using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Targets;
using PackageCheck.Core.Tests.Heuristics;
using PackageCheck.Core.Verdicts;
using static PackageCheck.Core.Tests.Heuristics.HeuristicFacts;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Verdicts;

public class TruthTableTests
{
    private static readonly InspectionResult Contents = new("P", "1.0.0", false, null, false, false, false, true, ["net10.0"], "h", []);

    private static readonly Evidence H05 = new("H05", EvidenceResult.Negative, EvidenceWeight.High, "nuget", "depends on Umbraco.Cms.Web.BackOffice");
    private static readonly Evidence H08 = new("H08", EvidenceResult.Negative, EvidenceWeight.High, "nupkg", "legacy package.manifest only");
    private static readonly Evidence H07 = new("H07", EvidenceResult.Positive, EvidenceWeight.High, "nupkg", "umbraco-package.json");
    private static readonly Evidence H09 = new("H09", EvidenceResult.Positive, EvidenceWeight.High, "nupkg", "implements IPackageManifestReader");
    private static readonly Evidence H06Fails = new("H06", EvidenceResult.Negative, EvidenceWeight.Low, "nupkg", "no lib/ TFM is net10.0");

    /// <summary>Real metadata heuristics over the ranges, plus the given contents evidence; contents are known when any is given.</summary>
    private static TableOutcome Decide(string[] ranges, Evidence[] extra, string target = "17.7.2", bool contentsKnown = false, bool hq = false)
    {
        var t = Target(target, target.StartsWith("13") ? "net8.0" : "net10.0");
        var f = Pkg("P", "1.0.0", ranges) with { Own = contentsKnown || extra.Any(e => e.Source == "nupkg") ? Contents : null };
        if (hq) f = f.With(Listing.Of("P", hq: true));
        IHeuristic[] metadata = [new H01RangeAllows(), new H02UpperBoundExcludes(), new H03OpenRangeFromOldMajor(), new H04UmbracoPath(), new H19HqStrictRange()];
        var ev = metadata.Where(h => h.AppliesTo(t)).SelectMany(h => h.Evaluate(f, t)).Concat(extra).ToList();
        return TruthTable.Decide(ev, f);
    }

    public static TheoryData<int, string[], Evidence[], Verdict, Confidence?> TableRows => new()
    {
        { 1, ["[16.0.0, 17.0.0)"], [H07], Verdict.Upgrade, Confidence.High },
        { 2, ["[17.0.0, 18.0.0)"], [H08, H09], Verdict.Unknown, null },
        { 3, ["[17.0.0, 18.0.0)"], [H08], Verdict.Suspicious, Confidence.Medium },
        { 4, ["13.8.1"], [H05], Verdict.Suspicious, Confidence.Medium },
        { 5, ["[17.0.0, 18.0.0)"], [H07], Verdict.Ok, Confidence.High },
        { 6, ["13.0.0"], [H07], Verdict.Ok, Confidence.High },
        { 7, ["[17.0.0, 18.0.0)"], [], Verdict.Ok, Confidence.Medium },
        { 8, ["14.0.0"], [], Verdict.Ok, Confidence.Low },
        { 9, ["13.0.0"], [H06Fails], Verdict.Suspicious, Confidence.Low },
        { 10, [], [], Verdict.NotUmbraco, null },
    };

    [Theory, MemberData(nameof(TableRows))]
    public void Row_N(int row, string[] ranges, Evidence[] ev, Verdict v, Confidence? c)
    {
        var o = Decide(ranges, ev);
        Assert.Equal((row, v, c), (o.Row, o.Verdict, o.Confidence));
        Assert.Equal(row is >= 5 and <= 8, o.Clears);
    }

    [Fact]
    public void Row9_WithoutH06Failing_IsOkLow() =>
        Assert.Equal((9, Verdict.Ok, (Confidence?)Confidence.Low), Pick(Decide(["13.0.0"], [])));

    [Fact]
    public void HighPositiveAndHighNegative_IsUnknownConflicting()
    {
        var o = Decide(["13.0.0"], [H05, H07]);
        Assert.Equal((2, Verdict.Unknown, (UnknownReason?)UnknownReason.ConflictingEvidence), (o.Row, o.Verdict, o.Reason));
    }

    [Fact]
    public void Row4_WithoutH03_StillMatches() // ruling R28: row 4 is "allows, open, with or without H03"
        => Assert.Equal(4, Decide(["14.0.0"], [H05]).Row);

    [Fact]
    public void HighPositive_WithoutKnownContents_DoesNotCount() =>
        Assert.Equal(7, Decide(["[17.0.0, 18.0.0)"], [H07 with { Source = "marketplace" }]).Row);

    [Fact]
    public void ServerOnlyPackage_MissingH07H09_IsNotSuspicious()
    {
        var bounded = Decide(["[17.0.0, 18.0.0)"], [], contentsKnown: true);
        var open14 = Decide(["14.0.0"], [], contentsKnown: true);
        Assert.Equal((7, Verdict.Ok), (bounded.Row, bounded.Verdict));
        Assert.Equal((8, Verdict.Ok), (open14.Row, open14.Verdict));
    }

    [Fact]
    public void HqStrictRange_Row7_StaysMedium() => // design 7.4: high needs H07, H09 or H16; H19 is not independent
        Assert.Equal((7, Verdict.Ok, (Confidence?)Confidence.Medium), Pick(Decide(["[17.0.0, 18.0.0)"], [], hq: true)));

    [Fact]
    public void OpenFromBelow14_Target13_IsRow8() => Assert.Equal(8, Decide(["10.0.0"], [], target: "13.9.0").Row);

    /// <summary>Ruling R28: every combination lands on a table row; the "no truth-table row" fallback is unreachable.</summary>
    [Fact]
    public void EveryCombination_HasARow()
    {
        string[][] rangeShapes = [["[16.0.0, 17.0.0)"], ["[17.0.0, 18.0.0)"], ["14.0.0"], ["13.0.0"], ["(, )"], ["[17.0.0, 18.0.0)", "13.0.0"], []];
        Evidence[] optional = [H05, H08, H07, H09, H06Fails];
        foreach (var target in new[] { "13.9.0", "17.7.2", "18.0.0-rc2" })
        foreach (var ranges in rangeShapes)
        foreach (var known in new[] { false, true })
        foreach (var hq in new[] { false, true })
        for (var mask = 0; mask < 1 << optional.Length; mask++)
        {
            var ev = optional.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            var o = Decide(ranges, ev, target, known, hq);
            Assert.True(o.Row >= 1, $"no row: target {target}, ranges [{string.Join(",", ranges)}], ev [{string.Join(",", ev.Select(e => e.Check))}]");
        }
    }

    private static (int, Verdict, Confidence?) Pick(TableOutcome o) => (o.Row, o.Verdict, o.Confidence);
}
