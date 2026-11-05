using PackageCheck.Core.Targets;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>H02: a declared range excludes the target (high negative, truth-table row 1). One line per excluding range.</summary>
public sealed class H02UpperBoundExcludes : IHeuristic
{
    public string Id => "H02";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        // No dependency group fits the target TFM: NuGet would refuse to install this version there at all.
        if (f.NoCompatibleGroup)
            yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.NuGet,
                $"no dependency group for {t.Tfm.GetShortFolderName()} (has {string.Join(", ", f.Details.DependencyGroups.Select(g => g.TargetFramework.GetShortFolderName()))})");
        foreach (var u in Ranges.Excluding(f, t))
            yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.NuGet,
                $"{u.UmbracoPackageId} {Ranges.Text(u)} excludes {t.Resolved.ToNormalizedString()}{Ranges.ViaSuffix(f)}");
    }
}
