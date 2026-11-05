using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Versioning;
using PackageCheck.Core.Facts;

namespace PackageCheck.Sources.Marketplace;

/// <summary>
/// Reads Marketplace listings. Never throws for a feed problem: 404 is "not listed" (null, no warning) and any other
/// failure is null plus one warning, because Marketplace failures never cause unknown or exit 3 (design 7.3).
/// Only free-text-free fields are read; README and description text never enter the tool (pitfall S3).
/// </summary>
public sealed class MarketplaceClient(HttpClient http, Action<string>? onWarning = null) : IMarketplaceSource
{
    public async Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(MarketplaceUrls.PackageDetail(id, target), HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.StatusCode == HttpStatusCode.NotFound) return null;
            if (!res.IsSuccessStatusCode)
            {
                onWarning?.Invoke($"Marketplace lookup for {id} failed with HTTP {(int)res.StatusCode}; continuing without it");
                return null;
            }
            await using var body = await res.Content.ReadAsStreamAsync(ct);
            var d = await JsonSerializer.DeserializeAsync<Detail>(body, cancellationToken: ct)
                    ?? throw new JsonException("empty body");
            return new MarketplaceInfo(
                ListingUrl(id),
                d.IsRetired ?? false,
                d.AlternateId,
                d.IsHq ?? false,
                NuGetVersion.TryParse(d.LatestForTarget, out var latest) ? latest : null,
                d.PackageIdForTarget);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        // IOException and InvalidDataException: a body cut off mid-read or a corrupt gzip stream (M2 review).
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException or NotSupportedException
                                       or IOException or InvalidDataException)
        {
            // Type name only: exception messages can carry response text from a third party.
            onWarning?.Invoke($"Marketplace lookup for {id} failed ({e.GetType().Name}); continuing without it");
            return null;
        }
    }

    private static string ListingUrl(string id) => $"https://marketplace.umbraco.com/package/{Uri.EscapeDataString(id.ToLowerInvariant())}";

    private sealed record Detail(
        [property: JsonPropertyName("isRetired")] bool? IsRetired,
        [property: JsonPropertyName("alternatePackageIdForRetiredPackage")] string? AlternateId,
        [property: JsonPropertyName("isHQ")] bool? IsHq,
        [property: JsonPropertyName("latestVersionNumberForProvidedUmbracoVersion")] string? LatestForTarget,
        [property: JsonPropertyName("packageIdForProvidedUmbracoVersion")] string? PackageIdForTarget);
}
