using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Facts;

namespace PackageCheck.Sources.Cache;

/// <summary>
/// Caches versions and details for <paramref name="maxAge"/> (6 h by default, design 7.5; null = any age, for offline).
/// Nupkgs are not cached here: they come from the global packages folder or a temp download, and their inspection is
/// cached by content hash (<see cref="CachingInspector"/>). A missing version is not cached, so a new release shows up.
/// </summary>
/// <param name="feed">Identifies the configured feeds in the key; redacted before use.</param>
public sealed class CachingMetadataSource(IPackageMetadataSource inner, FileCache cache, TimeSpan? maxAge, string feed) : IPackageMetadataSource
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(6);

    public async Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct)
    {
        var key = new CacheKey("versions", feed, id, null, null);
        if (cache.TryRead<VersionsDto>(key, maxAge, out var hit, h => h.Id is not null && h.Versions is not null && h.Versions.All(v => NuGetVersion.TryParse(v, out _))))
            return new PackageVersions(hit.Id, hit.Versions.Select(NuGetVersion.Parse).ToList());
        var fresh = await inner.GetVersionsAsync(id, ct);
        cache.Write(key, new VersionsDto(fresh.Id, fresh.Versions.Select(v => v.ToFullString()).ToList()));
        return fresh;
    }

    public async Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct)
    {
        var key = new CacheKey("details", feed, id, v.ToNormalizedString(), null);
        if (cache.TryRead<DetailsDto>(key, maxAge, out var hit, h => h.IsWhole)) return hit.ToDetails();
        var fresh = await inner.GetDetailsAsync(id, v, ct);
        if (fresh is not null) cache.Write(key, DetailsDto.From(fresh));
        return fresh;
    }

    public Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct) => inner.OpenNupkgAsync(id, v, ct);

    private sealed record VersionsDto(string Id, List<string> Versions);

    private sealed record DependencyDto(string Id, VersionRange Range);

    private sealed record GroupDto(NuGetFramework Tfm, List<DependencyDto> Dependencies);

    private sealed record DetailsDto(string Id, NuGetVersion Version, List<GroupDto> Groups, DeprecationInfo? Deprecation,
        DateTimeOffset? Published, bool Listed)
    {
        public static DetailsDto From(PackageDetails d) => new(d.Identity.Id, d.Identity.Version,
            d.DependencyGroups.Select(g => new GroupDto(g.TargetFramework, g.Packages.Select(p => new DependencyDto(p.Id, p.VersionRange)).ToList())).ToList(),
            d.Deprecation, d.Published, d.Listed);

        public bool IsWhole => Id is not null && Version is not null && Groups is not null
            && Groups.All(g => g?.Tfm is not null && g.Dependencies is not null && g.Dependencies.All(d => d?.Id is not null && d.Range is not null))
            && (Deprecation is null || Deprecation.Reasons is not null);

        public PackageDetails ToDetails() => new(new PackageIdentity(Id, Version),
            Groups.Select(g => new PackageDependencyGroup(g.Tfm, g.Dependencies.Select(p => new PackageDependency(p.Id, p.Range)))).ToList(),
            Deprecation, Published, Listed);
    }
}
