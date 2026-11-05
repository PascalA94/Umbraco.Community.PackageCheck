using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// Turns what inspection could not read into info evidence, so a verdict built on partial contents says so
/// (design 4: "over the size cap, stop and fall back to metadata-only evidence").
/// </summary>
public sealed class InspectionNotes : IHeuristic
{
    public string Id => "inspect";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t) =>
        (f.InspectionNotes ?? []).Select(n => new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.Nupkg, n));
}
