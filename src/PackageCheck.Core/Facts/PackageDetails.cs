using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace PackageCheck.Core.Facts;

/// <summary>
/// Registration facts for one version. <see cref="Published"/> is null when the feed gives none, and for unlisted
/// nuget.org versions, whose registration date is the 1900-01-01 sentinel rather than a real release date.
/// </summary>
public sealed record PackageDetails(
    PackageIdentity Identity,
    IReadOnlyList<PackageDependencyGroup> DependencyGroups,
    DeprecationInfo? Deprecation,
    DateTimeOffset? Published,
    bool Listed);

/// <summary>NuGet deprecation (H14): reasons as the feed spells them, plus the alternate package when one is named.</summary>
public sealed record DeprecationInfo(IReadOnlyList<string> Reasons, string? AlternateId, VersionRange? AlternateRange);
