using System.Security.Cryptography;
using System.Text;

namespace PackageCheck.Sources.Cache;

/// <summary>
/// Identifies one cache entry. The feed is stored only as a SHA-256 of its full URL: tokens can sit in userinfo, query
/// or path (security review M3), so no part of the URL is kept, while two feeds on one host still get separate entries.
/// Ids and versions compare case-insensitively, as everywhere in the tool.
/// </summary>
public sealed record CacheKey(string Kind, string Feed, string Id, string? Version, string? Extra)
{
    public string Normalised => string.Join('|',
        Kind, FeedHash(Feed), Id.ToLowerInvariant(), Version?.ToLowerInvariant() ?? "", Extra ?? "");

    public override string ToString() => Normalised;

    private static string FeedHash(string feed) =>
        feed.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(feed)))[..16].ToLowerInvariant();
}
