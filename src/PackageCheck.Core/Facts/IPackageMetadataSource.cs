using NuGet.Versioning;

namespace PackageCheck.Core.Facts;

/// <summary>
/// Package metadata and contents from the site's feeds (design 7.2, 7.3). Failures surface as
/// <see cref="PackageSourceException"/> carrying the unknown reason; an oversized nupkg as <see cref="NupkgTooLargeException"/>.
/// </summary>
public interface IPackageMetadataSource
{
    /// <summary>Every version the first feed holding <paramref name="id"/> knows, listed and unlisted, ascending.</summary>
    Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct);

    /// <summary>Details for one version, or null when the feed holding the id lacks that version.</summary>
    Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct);

    /// <summary>The nupkg as a file on disk (global packages folder first, design 7.5), or null when no feed has it.</summary>
    Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct);
}
