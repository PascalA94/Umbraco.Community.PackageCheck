using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H07: <c>staticwebassets/**/umbraco-package.json</c> in the package or a dependency (design 7.4; Workflow ships it in
/// its StaticAssets dependency). High positive. The detail names where it was found and its <c>BasePath</c>.
/// </summary>
public sealed class H07UmbracoPackageJson : IHeuristic
{
    public string Id => "H07";

    public bool AppliesTo(TargetVersion t) => t.RangeVersion.Major >= UmbracoFacts.NewBackofficeMajor;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (!AppliesTo(t) || f.Own is null) yield break;
        var hit = new[] { f.Own! }.Concat(f.Dependencies).FirstOrDefault(c => c.HasUmbracoPackageJson);
        if (hit is null) yield break;
        var basePath = hit.StaticWebAssetBasePath is { } bp ? $", BasePath {bp}" : "";
        yield return new Evidence(Id, EvidenceResult.Positive, EvidenceWeight.High, EvidenceSources.Nupkg,
            $"umbraco-package.json in {InspectionView.Name(hit)}{basePath}");
    }
}
