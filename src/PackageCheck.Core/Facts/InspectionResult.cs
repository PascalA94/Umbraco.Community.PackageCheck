namespace PackageCheck.Core.Facts;

/// <summary>
/// What a nupkg's contents say (design 7.2, filled by the Task 19 inspector and the Task 20 type scan).
/// <see cref="PackageId"/>, <see cref="Version"/> and <see cref="ManifestPaths"/> go beyond the design sketch so
/// evidence can name the file and the package it came from. <see cref="ContentHash"/> is the base64 SHA-512 of the nupkg.
/// </summary>
public sealed record InspectionResult(
    string PackageId,
    string Version,
    bool HasUmbracoPackageJson,
    string? StaticWebAssetBasePath,
    bool HasLegacyManifest,
    bool ReferencesManifestReader,
    bool ReferencesManagementApi,
    bool HasClientAssets,
    IReadOnlyList<string> LibTfms,
    string ContentHash,
    IReadOnlyList<string> ManifestPaths);
