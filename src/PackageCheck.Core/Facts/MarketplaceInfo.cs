using NuGet.Versioning;

namespace PackageCheck.Core.Facts;

/// <summary>A Marketplace listing as H12, H13 and H19 read it (design 7.4). Filled by the Task 16 client.</summary>
public sealed record MarketplaceInfo(
    string Url,
    bool IsRetired,
    string? AlternatePackageId,
    bool IsHq,
    NuGetVersion? LatestForTarget,
    string? PackageIdForTarget);
