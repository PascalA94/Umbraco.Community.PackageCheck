using NuGet.Versioning;

namespace PackageCheck.Core.Model;

public sealed record ProjectInfo(string Path, IReadOnlyList<string> Tfms, NuGetVersion? UmbracoVersion, IReadOnlyList<PackageRef> Packages);
