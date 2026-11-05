using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;

namespace PackageCheck.Core.Verdicts;

/// <summary>A truth-table result. Row 1's <see cref="Verdict.Upgrade"/> is a placeholder the engine resolves to upgrade or no-release.</summary>
public sealed record TableOutcome(int Row, Verdict Verdict, Confidence? Confidence, UnknownReason? Reason)
{
    /// <summary>A version "clears" the target when it lands on rows 5 to 8 (design 7.4).</summary>
    public bool Clears => Row is >= 5 and <= 8;
}

/// <summary>
/// Design 7.4's truth table for the current version, first match wins, with the row 4 and row 8 widenings and the
/// fallback of ruling R28 (docs/spikes.md, "Truth table changes"). Replacement by H13/H14 happens in the engine.
/// </summary>
public static class TruthTable
{
    public const int NoRow = -1;

    public static TableOutcome Decide(IReadOnlyList<Evidence> evidence, PackageFacts facts)
    {
        if (Has(evidence, "H02", e => e.IsHighNegative)) return new(1, Verdict.Upgrade, Confidence.High, null);
        if (facts.Umbraco.Count == 0) return new(10, Verdict.NotUmbraco, null, null);

        var open = facts.Umbraco.Any(u => !u.Range.HasUpperBound);
        var bounded = !open;
        var h03 = Has(evidence, "H03");
        var hq = Has(evidence, "H19", e => e.IsHighPositive);
        var highNeg = evidence.Any(e => e.IsHighNegative && e.Check is "H05" or "H08");
        // Contents evidence counts only when the contents were read (it is "n/a" with --no-inspect).
        var highPos = facts.ContentsKnown && evidence.Any(e => e.IsHighPositive && e.Check is "H07" or "H09");
        var h06Fails = evidence.Any(e => e.Check == "H06" && e.Result == EvidenceResult.Negative);

        if (highNeg && highPos) return new(2, Verdict.Unknown, null, UnknownReason.ConflictingEvidence);
        if (bounded && highNeg) return new(3, Verdict.Suspicious, Confidence.Medium, null);
        if (open && highNeg) return new(4, Verdict.Suspicious, Confidence.Medium, null);
        if ((bounded || hq) && highPos) return new(5, Verdict.Ok, Confidence.High, null);
        if (open && highPos) return new(6, Verdict.Ok, Confidence.High, null);
        // H19 never lifts row 7: "ok high needs H01 plus H07, H09 or H16" (design 7.4), and the Marketplace is not independent.
        if (bounded) return new(7, Verdict.Ok, Confidence.Medium, null);
        if (open && !h03) return new(8, Verdict.Ok, Confidence.Low, null);
        if (h03) return new(9, h06Fails ? Verdict.Suspicious : Verdict.Ok, Confidence.Low, null);
        return new(NoRow, Verdict.Unknown, null, UnknownReason.ConflictingEvidence);
    }

    private static bool Has(IReadOnlyList<Evidence> evidence, string check, Func<Evidence, bool>? also = null) =>
        evidence.Any(e => e.Check == check && (also?.Invoke(e) ?? true));
}
