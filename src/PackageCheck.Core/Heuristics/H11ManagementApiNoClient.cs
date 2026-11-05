using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H11 (cuttable #2): Management API references but no JS and no manifest anywhere in the package or its dependencies.
/// Low negative, evidence only: the truth table never reads it. Cutting it means deleting this file and its line in
/// <see cref="HeuristicSet"/>.
/// </summary>
public sealed class H11ManagementApiNoClient : IHeuristic
{
    public string Id => "H11";

    public bool AppliesTo(TargetVersion t) => t.RangeVersion.Major >= UmbracoFacts.NewBackofficeMajor;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (!AppliesTo(t) || f.Own is null) yield break;
        var all = f.OwnContents.Concat(f.Dependencies).ToList();
        if (!all.Any(c => InspectionView.ReferencesManagementApi(c, t.Tfm))) yield break;
        if (all.Any(c => c.HasClientAssets || c.HasUmbracoPackageJson || c.HasLegacyManifest)) yield break;
        yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.Low, EvidenceSources.Nupkg,
            "Management API references but no client assets or manifest");
    }
}
