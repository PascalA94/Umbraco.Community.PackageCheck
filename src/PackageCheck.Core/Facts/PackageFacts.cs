using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace PackageCheck.Core.Facts;

/// <summary>
/// Options for one collection: whether nupkgs may be opened (H06-H09, H11), the projects using the package, and whether
/// to ask the Marketplace (off for upgrade candidates, whose verdict the Marketplace never decides).
/// </summary>
public sealed record FactsOptions(bool Inspect, IReadOnlyList<string> ProjectDirectories, bool Marketplace = true);

/// <summary>
/// Everything the heuristics read about one (id, version) at one target. Heuristics are pure over this record; all I/O
/// happens in <see cref="FactsCollector"/> (docs/spikes.md, "Build decisions").
/// </summary>
/// <param name="ChosenGroupTfm">The dependency group NuGet would pick for the target TFM; ranges come only from it (ruling R21).</param>
/// <param name="Umbraco">Every <c>Umbraco.Cms.*</c> range found: the direct ones, or the walk's when there is none. Empty = no path (H04).</param>
/// <param name="UmbracoPathDependencies">Non-CMS dependencies with their own path to <c>Umbraco.Cms.*</c>; their contents count for H07/H09.</param>
/// <param name="ClearingVersions">Versions the engine found landing on truth-table rows 5-8 (Task 17); null until computed. H15 reads it.</param>
/// <param name="LatestPublished">Publish date of the newest version that has one (H18); null when the feed gives none.</param>
/// <param name="MissingDependencies">Dependency ids the walk could not resolve on any feed; with no path found, absence of a path is unproven.</param>
/// <param name="NoCompatibleGroup">The nuspec has dependency groups but none NuGet would pick for the target TFM: the version cannot be installed there.</param>
/// <param name="DependencyIds">Direct dependency ids from the chosen group, as the nuspec spells them.</param>
public sealed record PackageFacts(
    string Id,
    NuGetVersion Version,
    PackageDetails Details,
    NuGetFramework? ChosenGroupTfm,
    IReadOnlyList<UmbracoDependency> Umbraco,
    IReadOnlyList<PackageIdentity> UmbracoPathDependencies,
    IReadOnlyList<string> DependencyIds,
    InspectionResult? Own,
    IReadOnlyList<InspectionResult> Dependencies,
    MarketplaceInfo? Marketplace,
    IReadOnlyList<string> ProjectDirectories,
    IReadOnlyList<NuGetVersion> AllVersions,
    IReadOnlyList<NuGetVersion>? ClearingVersions = null,
    DateTimeOffset? LatestPublished = null,
    IReadOnlyList<string>? MissingDependencies = null,
    bool NoCompatibleGroup = false)
{
    public bool ContentsKnown => Own is not null;

    /// <summary>
    /// True only when contents are known and neither the package nor its inspected dependencies ship client assets, a
    /// manifest (new or legacy), a manifest reader or Management API references. H07/H09 are never discarded (ruling R31).
    /// </summary>
    public bool IsServerOnly => Own is not null && Dependencies.Prepend(Own).All(c =>
        !c.HasClientAssets && !c.HasUmbracoPackageJson && !c.HasLegacyManifest && !c.ReferencesManifestReader && !c.ReferencesManagementApi);
}
