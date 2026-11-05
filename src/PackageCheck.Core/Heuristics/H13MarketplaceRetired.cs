using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H13: retired, or renamed for the target's major, on the Marketplace. High negative when there is an alternate to
/// move to (carried as data, ruling R34), so the engine can say <c>replaced</c>; info when retired with no alternate.
/// The rename leg here is the one the API exposes (a different package id for the target's major, ruling R30);
/// renames only visible on the new id's listing come from seed overrides.
/// </summary>
public sealed class H13MarketplaceRetired : IHeuristic
{
    public string Id => "H13";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.Marketplace is not { } m) yield break;
        if (m.IsRetired)
        {
            yield return m.AlternatePackageId is null
                ? new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.Marketplace, "retired on the Marketplace, no alternate")
                : new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.Marketplace,
                    $"retired on the Marketplace; alternate {m.AlternatePackageId}", m.AlternatePackageId);
            yield break;
        }
        if (m.PackageIdForTarget is { } forTarget && !forTarget.Equals(f.Id, StringComparison.OrdinalIgnoreCase))
            yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.Marketplace,
                $"renamed on the Marketplace for Umbraco {t.Resolved.Major}: {forTarget}", forTarget);
    }
}
