using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Tests.Heuristics;

internal static class HeuristicFacts
{
    /// <summary>Facts for one package; <paramref name="ranges"/> are NuGet ranges on Umbraco.Cms.Core ("13.8.1" means >= 13.8.1).</summary>
    public static PackageFacts Pkg(string id, string version, params string[] ranges) => Build(id, version,
        ranges.Select(r => new UmbracoDependency(id, "Umbraco.Cms.Core", VersionRange.Parse(r), 1)).ToList());

    public static PackageFacts Build(string id, string version, IReadOnlyList<UmbracoDependency> umbraco,
        IReadOnlyList<string>? dependencyIds = null, DeprecationInfo? deprecation = null, IReadOnlyList<string>? allVersions = null,
        IReadOnlyList<string>? clearing = null, DateTimeOffset? latestPublished = null)
    {
        var v = NuGetVersion.Parse(version);
        var details = new PackageDetails(new PackageIdentity(id, v), [], deprecation, null, true);
        return new PackageFacts(id, v, details, null, umbraco, [],
            dependencyIds ?? umbraco.Select(u => u.UmbracoPackageId).ToList(),
            null, [], null, [], (allVersions ?? [version]).Select(NuGetVersion.Parse).ToList(),
            clearing?.Select(NuGetVersion.Parse).ToList(), latestPublished);
    }
}
