using NuGet.Versioning;

namespace PackageCheck.Core.Model;

public static class PackageRefs
{
    private static readonly string[] UmbracoIds = ["Umbraco.Cms", "Umbraco.Cms.Core"];

    /// <summary>The highest resolved version of Umbraco.Cms or Umbraco.Cms.Core, or null when neither resolves.</summary>
    public static NuGetVersion? UmbracoVersionOf(IEnumerable<PackageRef> packages) => packages
        .Where(p => p.Version is not null && UmbracoIds.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
        .Select(p => p.Version!)
        .DefaultIfEmpty()
        .Max();
}
