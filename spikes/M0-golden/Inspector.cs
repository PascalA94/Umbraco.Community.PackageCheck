// Nupkg inspection, reusing spikes S2 (paths, props) and S3 (metadata scan). Hostile-input rules (design section 4):
// the nupkg is read as a zip stream and nothing is extracted; DLL bytes go to System.Reflection.Metadata only, never
// Assembly.Load*/AssemblyLoadContext/MetadataLoadContext; unsafe entry names and cap breaches make the package unsafe-archive.
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using NuGet.Frameworks;

namespace M0Golden;

sealed record Contents(
    string Id,
    string Version,
    string? UnsafeReason,
    IReadOnlyList<NuGetFramework> LibTfms,
    string? ScannedLibFolder,
    IReadOnlyList<string> UmbracoPackageJson,
    IReadOnlyList<string> PackageManifest,
    int JsCount,
    IReadOnlyList<string> BasePaths,
    IReadOnlyList<string> ManifestReaders,
    int ManagementApiRefs)
{
    public bool HasClientAssets => JsCount > 0 || UmbracoPackageJson.Count > 0 || PackageManifest.Count > 0;
}

static partial class Inspector
{
    const long PerEntryCap = 50L * 1024 * 1024;
    const long TotalCap = 500L * 1024 * 1024;
    const int EntryCountCap = 20_000;

    /// <summary>Simple names of every DLL scanned, for the end-of-run no-load check (S3 Step 3).</summary>
    public static HashSet<string> ScannedAssemblyNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static int DllCount { get; private set; }

    [GeneratedRegex(@"^build[^/]*/Microsoft\.AspNetCore\.StaticWebAssets\.props$", RegexOptions.IgnoreCase)]
    private static partial Regex PropsPattern();

    static bool IsUnsafe(string name) =>
        name.StartsWith('/') || name.StartsWith('\\') || (name.Length > 1 && name[1] == ':') || name.Split('/', '\\').Contains("..");

    static string FileName(string entry) => entry.Split('/').Last();

    // R21: JS under staticwebassets/*, staticwebassets/App_Plugins/* and content/App_Plugins/*; root App_Plugins/ is the v8-13 layout.
    static bool IsBackofficeJs(string n) =>
        (n.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
        && (n.StartsWith("staticwebassets/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("content/App_Plugins/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("App_Plugins/", StringComparison.OrdinalIgnoreCase));

    public static Contents Inspect(string id, string version, string path, NuGetFramework targetTfm)
    {
        long total = 0;
        using var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
        if (zip.Entries.Count > EntryCountCap) return Unsafe(id, version, "entry count over cap");
        var names = zip.Entries.Select(e => e.FullName).ToList();
        var bad = names.FirstOrDefault(IsUnsafe);
        if (bad is not null) return Unsafe(id, version, $"unsafe entry name '{bad}'");

        byte[] Read(ZipArchiveEntry e)
        {
            // Central-directory sizes are not trusted: count bytes actually read (plan review P7).
            using var s = e.Open();
            var ms = new MemoryStream();
            var buf = new byte[81920]; int n; long entry = 0;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                entry += n; total += n;
                if (entry > PerEntryCap || total > TotalCap) throw new CapExceededException();
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }

        try
        {
            // Design 7.4 H07: staticwebassets/**/umbraco-package.json only.
            var upj = names.Where(n => n.StartsWith("staticwebassets/", StringComparison.OrdinalIgnoreCase)
                && FileName(n).Equals("umbraco-package.json", StringComparison.OrdinalIgnoreCase)).ToList();
            // **/package.manifest: matches App_Plugins/, content/App_Plugins/ and staticwebassets/ alike (S2 finding 2).
            var manifest = names.Where(n => FileName(n).Equals("package.manifest", StringComparison.OrdinalIgnoreCase)).ToList();
            var js = names.Count(IsBackofficeJs);

            var basePaths = new List<string>();
            foreach (var p in names.Where(n => PropsPattern().IsMatch(n)))
            {
                var doc = SafeXml.Parse(Encoding.UTF8.GetString(Read(zip.GetEntry(p)!)));
                basePaths.AddRange(doc.Descendants().Where(e => e.Name.LocalName == "BasePath").Select(e => e.Value.Trim()).Distinct());
            }

            var libFolders = names.Where(n => n.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)).Select(n => n.Split('/'))
                .Where(p => p.Length > 2).Select(p => p[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var libTfms = libFolders.Select(f => new LibFolder(f, NuGetFramework.ParseFolder(f))).ToList();
            // R23: scan only the lib group the target TFM selects.
            var nearest = NuGetFrameworkUtility.GetNearest(libTfms, targetTfm, x => x.Tfm) ?? new LibFolder(null, NuGetFramework.UnsupportedFramework);
            var readers = new List<string>();
            var mgmt = new HashSet<string>(StringComparer.Ordinal);
            if (nearest.Folder is not null)
            {
                var prefix = $"lib/{nearest.Folder}/";
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                             && !e.FullName[prefix.Length..].Contains('/') && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    DllCount++;
                    ScannedAssemblyNames.Add(Path.GetFileNameWithoutExtension(entry.Name));
                    using var pe = new PEReader(new MemoryStream(Read(entry)));
                    if (!pe.HasMetadata) continue;
                    var md = pe.GetMetadataReader();
                    if (md.IsAssembly) ScannedAssemblyNames.Add(md.GetString(md.GetAssemblyDefinition().Name));
                    readers.AddRange(ManifestReaders(md));
                    foreach (var h in md.TypeReferences)
                    {
                        var (ns, name) = FullName(md, h);
                        if (ns.StartsWith("Umbraco.Cms.Api.Management", StringComparison.Ordinal)) mgmt.Add($"{ns}.{name}");
                    }
                }
            }
            return new Contents(id, version, null, libTfms.Select(x => x.Tfm).ToList(), nearest.Folder, upj, manifest, js, basePaths, readers, mgmt.Count);
        }
        catch (CapExceededException) { return Unsafe(id, version, "byte cap exceeded while reading"); }
        catch (BadImageFormatException) { return Unsafe(id, version, "malformed DLL in the target lib group"); }
        catch (InvalidDataException) { return Unsafe(id, version, "corrupt zip entry"); }
    }

    /// <summary>
    /// H09 per ruling R23: a type defined here implements the interface TypeReference
    /// Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader whose resolution scope is AssemblyRef Umbraco.Infrastructure.
    /// The `PackageManifest` leg of design H09 is dropped: that name is also the v10-13 model in Umbraco.Cms.Core.Manifest.
    /// </summary>
    static IEnumerable<string> ManifestReaders(MetadataReader md)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            foreach (var ih in td.GetInterfaceImplementations())
            {
                var itf = md.GetInterfaceImplementation(ih).Interface;
                if (itf.Kind != HandleKind.TypeReference) continue;
                var tr = md.GetTypeReference((TypeReferenceHandle)itf);
                if (md.GetString(tr.Name) != "IPackageManifestReader" || md.GetString(tr.Namespace) != "Umbraco.Cms.Infrastructure.Manifest") continue;
                if (tr.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
                var asm = md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope);
                if (md.GetString(asm.Name) != "Umbraco.Infrastructure") continue;
                yield return $"{md.GetString(td.Namespace)}.{md.GetString(td.Name)}";
            }
        }
    }

    // Nested type references carry no namespace; take it from the enclosing type reference (S3).
    static (string Ns, string Name) FullName(MetadataReader md, TypeReferenceHandle h)
    {
        var tr = md.GetTypeReference(h);
        var name = md.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var (ons, oname) = FullName(md, (TypeReferenceHandle)tr.ResolutionScope);
            return (ons, $"{oname}+{name}");
        }
        return (md.GetString(tr.Namespace), name);
    }

    static Contents Unsafe(string id, string version, string why) => new(id, version, why, [], null, [], [], 0, [], [], 0);

    sealed class CapExceededException : Exception;

    sealed record LibFolder(string? Folder, NuGetFramework Tfm);
}
