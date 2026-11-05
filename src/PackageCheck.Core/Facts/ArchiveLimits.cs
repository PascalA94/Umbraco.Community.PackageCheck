namespace PackageCheck.Core.Facts;

/// <summary>
/// Caps for hostile nupkgs (design 4, plan review P7). Defined once in Core (preflight 1.31) so the download in
/// Sources and the inspector in Inspect enforce the same 250 MB limit.
/// </summary>
public sealed record ArchiveLimits(
    long MaxNupkgBytes = 250L * 1024 * 1024,
    int MaxEntries = 20_000,
    long MaxEntryBytes = 50L * 1024 * 1024,
    long MaxTotalBytes = 500L * 1024 * 1024)
{
    public static ArchiveLimits Default { get; } = new();
}
