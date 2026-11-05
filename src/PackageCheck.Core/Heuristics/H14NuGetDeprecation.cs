using PackageCheck.Core.Targets;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H14: NuGet deprecation. High negative with the alternate carried as data (ruling R34), so the engine can say
/// <c>replaced</c>; without an alternate there is nothing to replace with, so it is an info line only.
/// </summary>
public sealed class H14NuGetDeprecation : IHeuristic
{
    public string Id => "H14";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.Details.Deprecation is not { } dep) yield break;
        var reasons = dep.Reasons.Count == 0 ? "no reason given" : string.Join(", ", dep.Reasons);
        yield return dep.AlternateId is null
            ? new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.NuGet, $"deprecated on NuGet ({reasons}), no alternate")
            : new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.NuGet,
                $"deprecated on NuGet ({reasons}); alternate {dep.AlternateId}", dep.AlternateId);
    }
}
