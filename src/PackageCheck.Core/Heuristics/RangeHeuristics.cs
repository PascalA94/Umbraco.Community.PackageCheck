using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>Shared reading of a package's <c>Umbraco.Cms.*</c> ranges against a target's <see cref="TargetVersion.RangeVersion"/>.</summary>
internal static class Ranges
{
    public static IEnumerable<UmbracoDependency> Excluding(PackageFacts f, TargetVersion t) =>
        f.Umbraco.Where(u => !u.Range.Satisfies(t.RangeVersion));

    public static bool AllAllow(PackageFacts f, TargetVersion t) => f.Umbraco.Count > 0 && !Excluding(f, t).Any();

    public static IEnumerable<UmbracoDependency> Open(PackageFacts f) => f.Umbraco.Where(u => !u.Range.HasUpperBound);

    /// <summary>Names the packages the H04 walk went through, so a transitive range is never mistaken for the package's own.</summary>
    public static string ViaSuffix(PackageFacts f) => f.Umbraco.Any(u => u.Depth > 1)
        ? $" (H04 via {string.Join(", ", f.Umbraco.Select(u => u.ViaPackageId).Distinct(StringComparer.OrdinalIgnoreCase))})"
        : "";

    public static string Text(UmbracoDependency u) => u.Range.ToNormalizedString();
}
