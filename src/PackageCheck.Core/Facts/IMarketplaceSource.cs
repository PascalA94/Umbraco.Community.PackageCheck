using NuGet.Versioning;

namespace PackageCheck.Core.Facts;

/// <summary>
/// The Umbraco Marketplace listing for a package at a target (H12, H13, H19). Returns null when the package is not
/// listed or the call fails: Marketplace failures never make a verdict unknown and never cause exit 3 (design 7.3).
/// </summary>
public interface IMarketplaceSource
{
    Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct);
}
