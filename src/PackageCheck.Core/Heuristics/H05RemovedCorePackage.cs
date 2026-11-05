using PackageCheck.Core.Targets;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>H05: depends on a core package the new backoffice removed (high negative). Only meaningful for 14+ targets.</summary>
public sealed class H05RemovedCorePackage : IHeuristic
{
    public string Id => "H05";

    public bool AppliesTo(TargetVersion t) => t.RangeVersion.Major >= UmbracoFacts.NewBackofficeMajor;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (!AppliesTo(t)) yield break;
        var removed = f.Umbraco.Select(u => u.UmbracoPackageId).Concat(f.DependencyIds)
            .FirstOrDefault(id => UmbracoFacts.RemovedBackofficePackages.Contains(id, StringComparer.OrdinalIgnoreCase));
        if (removed is null) yield break;
        var canonical = UmbracoFacts.RemovedBackofficePackages.First(r => r.Equals(removed, StringComparison.OrdinalIgnoreCase));
        yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.NuGet,
            $"depends on {canonical}, removed in the new backoffice");
    }
}
