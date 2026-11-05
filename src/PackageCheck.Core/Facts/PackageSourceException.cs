using NuGet.Versioning;
using PackageCheck.Core.Model;

namespace PackageCheck.Core.Facts;

/// <summary>
/// A feed could not answer for a package. <paramref name="safeMessage"/> is built by the source from redacted feed
/// URLs only; no inner exception is kept, because NuGet and HttpClient messages can carry URLs with userinfo.
/// </summary>
public class PackageSourceException(UnknownReason reason, string safeMessage) : Exception(safeMessage)
{
    public UnknownReason Reason { get; } = reason;
}

/// <summary>The nupkg exceeds <see cref="ArchiveLimits.MaxNupkgBytes"/>; callers fall back to metadata-only evidence.</summary>
public class NupkgTooLargeException(string id, NuGetVersion v, long limit)
    : Exception($"{id} {v.ToNormalizedString()} is larger than the {limit} byte nupkg limit; using metadata only")
{
    public string Id { get; } = id;
    public NuGetVersion Version { get; } = v;
    public long Limit { get; } = limit;
}
