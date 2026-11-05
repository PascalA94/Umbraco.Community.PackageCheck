using NuGet.Versioning;
using PackageCheck.Core.Facts;

namespace PackageCheck.Sources.Cache;

/// <summary>
/// Caches Marketplace listings for 24 h (design 7.5; null max age = any, for offline). A null answer is "not listed" or
/// "the call failed" and the two cannot be told apart, so it is never cached.
/// </summary>
public sealed class CachingMarketplaceSource(IMarketplaceSource inner, FileCache cache, TimeSpan? maxAge) : IMarketplaceSource
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

    public async Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct)
    {
        var key = new CacheKey("marketplace", "marketplace", id, target.ToNormalizedString(), null);
        if (cache.TryRead<MarketplaceInfo>(key, maxAge, out var hit, h => h.Url is not null)) return hit;
        var fresh = await inner.GetAsync(id, target, ct);
        if (fresh is not null) cache.Write(key, fresh);
        return fresh;
    }
}

/// <summary>Offline Marketplace: nothing but what the cache already holds (design 7.5).</summary>
public sealed class OfflineMarketplaceSource : IMarketplaceSource
{
    public static OfflineMarketplaceSource Instance { get; } = new();

    public Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct) => Task.FromResult<MarketplaceInfo?>(null);
}
