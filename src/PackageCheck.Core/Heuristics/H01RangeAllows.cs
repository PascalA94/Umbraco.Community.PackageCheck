using PackageCheck.Core.Targets;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>H01: every declared <c>Umbraco.Cms.*</c> range allows the target. Medium when all are bounded, low when any is open.</summary>
public sealed class H01RangeAllows : IHeuristic
{
    public string Id => "H01";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (!Ranges.AllAllow(f, t)) yield break;
        var weight = Ranges.Open(f).Any() ? EvidenceWeight.Low : EvidenceWeight.Medium;
        var ranges = string.Join(" / ", f.Umbraco.Select(Ranges.Text).Distinct());
        yield return new Evidence(Id, EvidenceResult.Positive, weight, EvidenceSources.NuGet,
            $"Umbraco.Cms.* {ranges} allows {t.Resolved.ToNormalizedString()}{Ranges.ViaSuffix(f)}");
    }
}
