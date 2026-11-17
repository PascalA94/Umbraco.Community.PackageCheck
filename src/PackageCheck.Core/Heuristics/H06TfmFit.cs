using NuGet.Frameworks;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H06: TFM fit. Ruling R1: it fails when no <c>lib/</c> TFM is compatible with the target major's own TFM (net10.0 for 17)
/// and at most one .NET release behind it, even if an older TFM would still restore; low alone, it only decides row 9.
/// Without inspection the nuspec dependency-group TFMs stand in. A package with neither gives no H06 evidence: nothing is known.
/// </summary>
public sealed class H06TfmFit : IHeuristic
{
    public string Id => "H06";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        var (tfms, source, what) = f.Own is { LibTfms.Count: > 0 } own
            ? (own.LibTfms.Select(Frameworks.SafeParseFolder).Where(x => !x.IsUnsupported).ToList(), EvidenceSources.Nupkg, "lib")
            : (f.Details.DependencyGroups.Select(g => g.TargetFramework).Where(x => !x.IsAny).ToList(), EvidenceSources.NuGet, "dependency group");
        if (tfms.Count == 0) yield break;

        var target = t.Tfm.GetShortFolderName();
        if (tfms.Any(x => Fits(t.Tfm, x)))
        {
            yield return new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, source, $"{what} fits {target}");
            yield break;
        }
        var has = string.Join(", ", tfms.Select(x => x.GetShortFolderName()).Distinct().Order(StringComparer.Ordinal));
        yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.Low, source, $"no {what} for {target} (has {has})");
    }

    // NuGet compatibility, plus a floor for .NET: a lib built for the previous .NET release still counts (net10.0 fits a
    // net11.0 target), an older one does not (net8.0 on net10.0 keeps ruling R1). netstandard follows NuGet's own rules.
    private static bool Fits(NuGetFramework target, NuGetFramework lib) =>
        DefaultCompatibilityProvider.Instance.IsCompatible(target, lib)
        && (lib.Framework != FrameworkConstants.FrameworkIdentifiers.NetCoreApp || lib.Version.Major >= target.Version.Major - 1);
}
