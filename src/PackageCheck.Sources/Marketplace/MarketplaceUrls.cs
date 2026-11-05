using NuGet.Versioning;

namespace PackageCheck.Sources.Marketplace;

/// <summary>
/// The only Marketplace API URL the tool builds: the read-only package detail. The API's package validation endpoint
/// is never called (global constraints), and keeping one builder makes that checkable.
/// </summary>
public static class MarketplaceUrls
{
    public static Uri PackageDetail(string id, NuGetVersion target) =>
        new($"https://api.marketplace.umbraco.com/api/v1.0/packages/{Uri.EscapeDataString(id)}?SupportsUmbracoVersionNumber={Uri.EscapeDataString(target.ToNormalizedString())}");
}
