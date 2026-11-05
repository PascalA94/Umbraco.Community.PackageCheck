using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H19: an HQ package (Marketplace <c>isHQ</c>) whose ranges are all bounded and allow the target. HQ ships per-major
/// lines with deliberate ranges, so H01 counts as high (design 7.4, truth-table row 5 "bounded or HQ").
/// </summary>
public sealed class H19HqStrictRange : IHeuristic
{
    public string Id => "H19";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.Marketplace is not { IsHq: true } || !Ranges.AllAllow(f, t) || Ranges.Open(f).Any()) yield break;
        yield return new Evidence(Id, EvidenceResult.Positive, EvidenceWeight.High, EvidenceSources.Marketplace,
            "HQ package with a bounded range that allows the target: H01 raised to high");
    }
}
