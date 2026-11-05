using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using NuGet.Packaging.Core;
using PackageCheck.Core;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Inspect;

/// <summary>
/// Inspects a nupkg as a zip stream (design 4, 7.2; spikes S2, S3). Every nupkg is hostile: nothing is extracted, no
/// assembly is loaded, entry names are checked before any entry is opened, and every entry read goes through
/// <see cref="CountingReadStream"/>. Manifests are recorded by name only and never parsed.
/// The result does not depend on the target, so Task 23 can cache it by content hash.
/// </summary>
public sealed partial class NupkgInspector(ArchiveLimits limits) : INupkgInspector
{
    [GeneratedRegex(@"^build[^/]*/Microsoft\.AspNetCore\.StaticWebAssets\.props$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PropsPattern();

    public async Task<InspectionResult> InspectAsync(PackageIdentity id, NupkgHandle nupkg, CancellationToken ct)
    {
        // The source checks the size before download too; this covers handles from any other origin (cache, tests).
        var length = new FileInfo(nupkg.FilePath).Length;
        if (length > limits.MaxNupkgBytes) throw new NupkgTooLargeException(id.Id, id.Version, limits.MaxNupkgBytes);

        await using var file = new FileStream(nupkg.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = Convert.ToBase64String(await SHA512.HashDataAsync(file, ct).ConfigureAwait(false));
        file.Position = 0;

        try
        {
            ArchiveGuard.PreCheck(file, limits);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            ArchiveGuard.Check(zip, limits);
            return Inspect(id, zip, hash, ct);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            // A corrupt central directory or a deflate stream that does not match its header: the archive cannot be
            // trusted, and the message from the zip reader is not shown because it can quote package-controlled bytes.
            throw new PackageSourceException(UnknownReason.UnsafeArchive, "archive is not a readable zip");
        }
    }

    private InspectionResult Inspect(PackageIdentity id, ZipArchive zip, string hash, CancellationToken ct)
    {
        var budget = new ByteBudget(limits.MaxTotalBytes);
        var notes = new List<string>();
        var names = zip.Entries.Select(e => e.FullName).ToList();

        // Truth table changes (H07): only staticwebassets/**/umbraco-package.json is served by the static web assets pipeline.
        var umbracoPackageJson = names.Where(n => n.StartsWith("staticwebassets/", StringComparison.OrdinalIgnoreCase)
            && FileName(n).Equals("umbraco-package.json", StringComparison.OrdinalIgnoreCase)).ToList();
        // S2 finding 2: legacy manifests sit under App_Plugins/, content/App_Plugins/ or staticwebassets/; match by file name.
        var legacy = names.Where(n => FileName(n).Equals("package.manifest", StringComparison.OrdinalIgnoreCase)).ToList();

        string? basePath = null;
        foreach (var entry in zip.Entries.Where(e => PropsPattern().IsMatch(e.FullName)))
        {
            ct.ThrowIfCancellationRequested();
            var bytes = ReadCounted(entry, budget, notes);
            if (bytes is null) continue;
            try
            {
                basePath ??= StaticWebAssetsProps.ReadBasePath(bytes);
            }
            catch (XmlException)
            {
                notes.Add($"{PackageText.Safe(entry.FullName)} not read: malformed or declares a DTD");
            }
        }

        var libScans = ScanLibs(zip, budget, notes, ct);

        return new InspectionResult(
            id.Id,
            id.Version.ToNormalizedString(),
            HasUmbracoPackageJson: umbracoPackageJson.Count > 0,
            StaticWebAssetBasePath: basePath is null ? null : PackageText.Safe(basePath),
            HasLegacyManifest: legacy.Count > 0,
            ReferencesManifestReader: libScans.Any(x => x.ImplementsManifestReader),
            ReferencesManagementApi: libScans.Any(x => x.ReferencesManagementApi),
            HasClientAssets: names.Any(IsClientScript),
            LibTfms: LibFolders(names, notes),
            ContentHash: hash,
            ManifestPaths: [.. umbracoPackageJson.Concat(legacy).Select(PackageText.Safe)],
            Notes: notes,
            LibScans: libScans);
    }

    // Top-level DLLs of each lib/<tfm>/ folder, as NuGet would copy them (S3). Bytes go to System.Reflection.Metadata only.
    private List<LibScan> ScanLibs(ZipArchive zip, ByteBudget budget, List<string> notes, CancellationToken ct)
    {
        var scans = new List<LibScan>();
        var dlls = zip.Entries
            .Select(e => (Entry: e, Parts: e.FullName.Split('/')))
            .Where(x => x.Parts.Length == 3 && x.Parts[0].Equals("lib", StringComparison.OrdinalIgnoreCase)
                && x.Parts[2].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(x => Frameworks.IsRecognised(x.Parts[1]))
            .GroupBy(x => x.Parts[1], StringComparer.OrdinalIgnoreCase);
        foreach (var folder in dlls)
        {
            bool reader = false, management = false;
            foreach (var (entry, _) in folder)
            {
                ct.ThrowIfCancellationRequested();
                using var bytes = ReadCounted(entry, budget, notes);
                if (bytes is null) continue;
                var scan = MetadataTypeScanner.Scan(bytes);
                reader |= scan.ReferencesManifestReader;
                management |= scan.ReferencesManagementApi;
            }
            scans.Add(new LibScan(folder.Key, reader, management));
        }
        return scans;
    }

    /// <summary>
    /// Reads a whole entry into memory through the byte counter, or returns null with a note when the central directory
    /// already declares it over the per-entry cap. The declared size is used only to skip; a lying size is caught while
    /// reading. Reading to the end (rather than letting a parser stop early) means every opened entry is charged in full
    /// against the caps, and parsers never see a stream they could pull more from.
    /// </summary>
    private MemoryStream? ReadCounted(ZipArchiveEntry entry, ByteBudget budget, List<string> notes)
    {
        if (entry.Length > limits.MaxEntryBytes)
        {
            notes.Add($"{PackageText.Safe(entry.FullName)} skipped: over the per-entry size cap");
            return null;
        }
        using var counted = new CountingReadStream(entry.Open(), limits.MaxEntryBytes, budget);
        var buffer = new MemoryStream();
        counted.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }

    private static string FileName(string entry) => entry[(entry.LastIndexOfAny(['/', '\\']) + 1)..];

    // Ruling R21: JS under staticwebassets/ (any depth), content/App_Plugins/ and root App_Plugins/ (the v8-13 layout).
    private static bool IsClientScript(string n) =>
        (n.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
        && (n.StartsWith("staticwebassets/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("content/App_Plugins/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("App_Plugins/", StringComparison.OrdinalIgnoreCase));

    // lib/<tfm>/<file>: a folder counts once it holds a file, including NuGet's empty-folder marker "_._".
    // Folders NuGet cannot parse as a framework are dropped with a note (security review M3, S3); restore ignores them too.
    private static List<string> LibFolders(IEnumerable<string> names, List<string> notes)
    {
        var folders = names
            .Select(n => n.Split('/'))
            .Where(p => p.Length > 2 && p[0].Equals("lib", StringComparison.OrdinalIgnoreCase) && p[1].Length > 0)
            .Select(p => p[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var bad in folders.Where(f => !Frameworks.IsRecognised(f)))
            notes.Add($"lib/{PackageText.Safe(bad)}/ skipped: not a recognised target framework");
        return folders.Where(Frameworks.IsRecognised).Select(PackageText.Safe).ToList();
    }
}
