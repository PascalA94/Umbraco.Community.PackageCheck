using NuGet.Frameworks;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// Reads type-scan results the way ruling R23 says: only the <c>lib/</c> folder NuGet would pick for the target TFM
/// counts. An <see cref="InspectionResult"/> without per-folder scans falls back to its flat flags.
/// </summary>
internal static class InspectionView
{
    public static bool ImplementsManifestReader(InspectionResult r, NuGetFramework tfm) =>
        r.LibScans is null ? r.ReferencesManifestReader : TargetLib(r, tfm)?.ImplementsManifestReader ?? false;

    public static bool ReferencesManagementApi(InspectionResult r, NuGetFramework tfm) =>
        r.LibScans is null ? r.ReferencesManagementApi : TargetLib(r, tfm)?.ReferencesManagementApi ?? false;

    private static LibScan? TargetLib(InspectionResult r, NuGetFramework tfm) =>
        NuGetFrameworkUtility.GetNearest(r.LibScans!.Where(s => Frameworks.IsRecognised(s.Tfm)), tfm, s => Frameworks.SafeParseFolder(s.Tfm));

    public static string Name(InspectionResult r) => $"{r.PackageId} {r.Version}";
}
