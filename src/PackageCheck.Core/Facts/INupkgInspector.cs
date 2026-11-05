using NuGet.Packaging.Core;

namespace PackageCheck.Core.Facts;

/// <summary>
/// Reads what a nupkg contains without extracting it or loading any assembly (design 4, 7.2). Hostile archives raise
/// <see cref="PackageSourceException"/> with reason <c>UnsafeArchive</c>; an oversized nupkg raises
/// <see cref="NupkgTooLargeException"/> so the caller falls back to metadata-only evidence.
/// </summary>
public interface INupkgInspector
{
    Task<InspectionResult> InspectAsync(PackageIdentity id, NupkgHandle nupkg, CancellationToken ct);
}
