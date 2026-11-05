using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Sources.NuGet;

namespace PackageCheck.Sources.Cache;

/// <summary>
/// The feed in <c>--offline</c> mode (design 7.5): no network at all. Metadata always fails with reason <c>offline</c>,
/// so only a <see cref="CachingMetadataSource"/> in front of it can answer; nupkgs come from the global packages folder
/// only. The NuGet source is never constructed in offline mode.
/// </summary>
public sealed class OfflineMetadataSource(string globalPackagesFolder) : IPackageMetadataSource
{
    public Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct) =>
        throw new PackageSourceException(UnknownReason.Offline, $"offline: {id} is not in the cache");

    public Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct) =>
        throw new PackageSourceException(UnknownReason.Offline, $"offline: {id} {v.ToNormalizedString()} is not in the cache");

    public Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct)
    {
        NuGetMetadataSource.ValidateId(id);
        var info = NuGetMetadataSource.GlobalPackagesFile(globalPackagesFolder, id, v);
        return Task.FromResult(info is null ? null : new NupkgHandle(info.FullName, IsTemporary: false, info.Length));
    }
}
