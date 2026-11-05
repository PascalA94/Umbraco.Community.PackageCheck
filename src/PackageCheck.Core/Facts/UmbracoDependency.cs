using NuGet.Versioning;

namespace PackageCheck.Core.Facts;

/// <summary>
/// A <c>Umbraco.Cms.*</c> range a package declares, directly or through the H04 walk. <paramref name="Depth"/> counts
/// hops from the package: a direct dependency is 1 and the walk stops at 3 (design 7.4 H04, rulings R3 and R34).
/// </summary>
public sealed record UmbracoDependency(string ViaPackageId, string UmbracoPackageId, VersionRange Range, int Depth);
