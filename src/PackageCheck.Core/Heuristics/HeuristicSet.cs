using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>The heuristics the engine runs, in report order. H15 is added by the engine after the upgrade walk.</summary>
public static class HeuristicSet
{
    /// <param name="versions">The majors table H18 needs; without it H18 is left out.</param>
    public static IReadOnlyList<IHeuristic> Metadata(UmbracoVersionsTable? versions) =>
    [
        new H01RangeAllows(),
        new H02UpperBoundExcludes(),
        new H03OpenRangeFromOldMajor(),
        new H04UmbracoPath(),
        new H05RemovedCorePackage(),
        new H12MarketplaceLatest(),
        new H13MarketplaceRetired(),
        new H14NuGetDeprecation(),
        .. versions is null ? Array.Empty<IHeuristic>() : [new H18StaleRelease(versions)],
        new H19HqStrictRange(),
    ];
}
