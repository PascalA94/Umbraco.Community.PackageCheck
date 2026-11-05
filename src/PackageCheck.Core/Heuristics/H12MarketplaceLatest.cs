using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H12: what the Marketplace says is the latest version for the target. Info only: the Marketplace derives it from the
/// same NuGet ranges, so it is not independent evidence and its badges are often wrong (design 7.4, scope research).
/// </summary>
public sealed class H12MarketplaceLatest : IHeuristic
{
    public string Id => "H12";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.Marketplace?.LatestForTarget is not { } latest) yield break;
        yield return new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.Marketplace,
            $"Marketplace says {latest.ToNormalizedString()} supports {t.Resolved.ToNormalizedString()}");
    }
}
