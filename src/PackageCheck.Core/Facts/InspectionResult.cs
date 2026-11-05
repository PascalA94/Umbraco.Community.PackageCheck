namespace PackageCheck.Core.Facts;

/// <summary>
/// What a nupkg's contents say (design 7.2, filled by the Task 19 inspector and the Task 20 type scan).
/// <see cref="PackageId"/>, <see cref="Version"/> and <see cref="ManifestPaths"/> go beyond the design sketch so
/// evidence can name the file and the package it came from. <see cref="ContentHash"/> is the base64 SHA-512 of the nupkg.
/// <see cref="Notes"/> are evidence notes about what could not be read (an entry over its cap, props with a DTD); null
/// or empty when everything inspected was read. <see cref="LibScans"/> holds the type scan per <c>lib/</c> folder, so the
/// caller can apply ruling R23 (only the lib group the target TFM selects counts) while this result stays
/// target-independent and cacheable by content hash; the two flags above are the OR over every folder.
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
    IReadOnlyList<string> ManifestPaths,
    IReadOnlyList<string>? Notes = null,
    IReadOnlyList<LibScan>? LibScans = null);

/// <summary>Type-scan results for the top-level DLLs of one <c>lib/&lt;tfm&gt;/</c> folder (ruling R23, H09 and H11).</summary>
public sealed record LibScan(string Tfm, bool ImplementsManifestReader, bool ReferencesManagementApi);
