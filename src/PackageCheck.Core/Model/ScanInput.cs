using NuGet.Versioning;

namespace PackageCheck.Core.Model;

public enum TransitiveStatus { NotRequested, FromLockFile, FromMsBuild, Unavailable }

public sealed record ScanInput(string SitePath, IReadOnlyList<ProjectInfo> Projects, IReadOnlyList<string> Warnings, TransitiveStatus Transitive)
{
    /// <summary>
    /// The highest Umbraco.Cms / Umbraco.Cms.Core version across projects; null when no project resolves one.
    /// Derived rather than stored so it cannot drift from the projects it summarises.
    /// </summary>
    public NuGetVersion? SiteUmbracoVersion => Projects.Select(p => p.UmbracoVersion).Max();
}
