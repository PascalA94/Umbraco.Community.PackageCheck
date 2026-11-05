using NuGet.Versioning;

namespace PackageCheck.Core.Facts;

/// <summary>All versions of a package, listed and unlisted, sorted ascending.</summary>
public sealed record PackageVersions(string Id, IReadOnlyList<NuGetVersion> Versions);
