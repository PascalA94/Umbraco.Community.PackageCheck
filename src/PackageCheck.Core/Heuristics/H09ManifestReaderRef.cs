using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H09 (ruling R23): a type in the target-TFM <c>lib/</c> DLLs of the package or a dependency implements
/// <c>IPackageManifestReader</c>, Umbraco's v14+ way to register a backoffice manifest from C#. High positive.
/// Known limitation, documented not guarded: a reader that is implemented but never registered still counts.
/// </summary>
public sealed class H09ManifestReaderRef : IHeuristic
{
    public string Id => "H09";

    public bool AppliesTo(TargetVersion t) => t.RangeVersion.Major >= UmbracoFacts.NewBackofficeMajor;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (!AppliesTo(t) || f.Own is null) yield break;
        var hit = new[] { f.Own! }.Concat(f.Dependencies).FirstOrDefault(c => InspectionView.ImplementsManifestReader(c, t.Tfm));
        if (hit is null) yield break;
        yield return new Evidence(Id, EvidenceResult.Positive, EvidenceWeight.High, EvidenceSources.Nupkg,
            $"IPackageManifestReader implemented in {InspectionView.Name(hit)}");
    }
}
