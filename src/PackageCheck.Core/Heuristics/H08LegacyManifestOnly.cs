using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H08: a legacy <c>package.manifest</c> with no new registration. Ruling R23: only the package's own H07 or H09 (its own
/// contents, including asset-only dependencies under R29) suppresses it, so dual-mode packages such as Crumpled.RobotsTxt
/// pass; a dependency's new registration next to an own legacy manifest is conflicting evidence (row 2), not a pass.
/// JS with neither manifest file is indeterminate and never fires H08 (S2 finding 1).
/// </summary>
public sealed class H08LegacyManifestOnly : IHeuristic
{
    public string Id => "H08";

    public bool AppliesTo(TargetVersion t) => t.RangeVersion.Major >= UmbracoFacts.NewBackofficeMajor;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (!AppliesTo(t)) yield break;
        var own = f.OwnContents.ToList();
        if (!own.Any(c => c.HasLegacyManifest)) yield break;
        if (own.Any(c => c.HasUmbracoPackageJson || InspectionView.ImplementsManifestReader(c, t.Tfm))) yield break;
        yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.Nupkg, "legacy package.manifest only");
    }
}
