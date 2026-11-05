using System.Security.Cryptography;
using NuGet.Packaging.Core;
using PackageCheck.Core.Facts;

namespace PackageCheck.Sources.Cache;

/// <summary>
/// Caches inspection results by id, version and the nupkg's SHA-512, with no expiry: the same bytes always inspect the
/// same. Failures (unsafe archive, over the cap) are not cached.
/// </summary>
public sealed class CachingInspector(INupkgInspector inner, FileCache cache, int cacheVersion = CachingInspector.InspectionCacheVersion)
    : INupkgInspector
{
    /// <summary>Bump whenever what the inspector records, or a heuristic's reading of it, changes; old entries are then ignored.</summary>
    public const int InspectionCacheVersion = 2; // 2: unparseable lib folders dropped, package text sanitised (M3 review)

    public async Task<InspectionResult> InspectAsync(PackageIdentity id, NupkgHandle nupkg, CancellationToken ct)
    {
        string hash;
        await using (var file = new FileStream(nupkg.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            hash = Convert.ToBase64String(await SHA512.HashDataAsync(file, ct));
        var key = new CacheKey("inspect", "", id.Id, id.Version.ToNormalizedString(), $"{hash}|v{cacheVersion}");
        if (cache.TryRead<InspectionResult>(key, maxAge: null, out var hit, IsWhole)) return hit;
        var fresh = await inner.InspectAsync(id, nupkg, ct);
        cache.Write(key, fresh);
        return fresh;
    }

    private static bool IsWhole(InspectionResult r) =>
        r.PackageId is not null && r.Version is not null && r.LibTfms is not null && r.ContentHash is not null && r.ManifestPaths is not null;
}
