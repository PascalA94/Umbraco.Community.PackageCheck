using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PackageCheck.Inspect;

/// <summary>What one DLL's metadata says (H09, H11). <see cref="ReferencesManifestReader"/> means "a type here implements it" (R23).</summary>
public sealed record TypeRefScan(bool ReferencesManifestReader, bool ReferencesManagementApi);

/// <summary>
/// Reads type metadata from DLL bytes with <see cref="PEReader"/> only: the assembly is never loaded, so package code
/// cannot run (design 4, spike S3). The banned-API analyzer (BannedSymbols.txt) keeps it that way at build time.
/// </summary>
public static class MetadataTypeScanner
{
    private static readonly TypeRefScan Nothing = new(false, false);

    public static TypeRefScan Scan(Stream peBytes)
    {
        try
        {
            using var pe = new PEReader(peBytes, PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchEntireImage);
            if (!pe.HasMetadata) return Nothing;
            var md = pe.GetMetadataReader();
            return new TypeRefScan(ImplementsManifestReader(md), ReferencesManagementApi(md));
        }
        catch (Exception e) when (e is not (OperationCanceledException or OutOfMemoryException))
        {
            // Malformed metadata raises BadImageFormatException, InvalidOperationException, OverflowException and others
            // (security review M3 fuzzing). A DLL we cannot read is no evidence; it must never abort the scan.
            return Nothing;
        }
    }

    /// <summary>
    /// Ruling R23: a type defined here implements the interface TypeReference
    /// <c>Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader</c> resolved from assembly <c>Umbraco.Infrastructure</c>.
    /// A bare reference, or the <c>PackageManifest</c> model (also the v10-13 name in Core.Manifest), does not count.
    /// </summary>
    private static bool ImplementsManifestReader(MetadataReader md)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            foreach (var ih in md.GetTypeDefinition(tdh).GetInterfaceImplementations())
            {
                var itf = md.GetInterfaceImplementation(ih).Interface;
                if (itf.Kind != HandleKind.TypeReference) continue;
                var tr = md.GetTypeReference((TypeReferenceHandle)itf);
                if (!md.StringComparer.Equals(tr.Name, "IPackageManifestReader")
                    || !md.StringComparer.Equals(tr.Namespace, "Umbraco.Cms.Infrastructure.Manifest")
                    || tr.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
                var asm = md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope);
                if (md.StringComparer.Equals(asm.Name, "Umbraco.Infrastructure")) return true;
            }
        }
        return false;
    }

    // H11: any type reference in a namespace starting Umbraco.Cms.Api.Management. Nested references carry no namespace,
    // so the outermost enclosing reference's namespace is used (S3).
    private static bool ReferencesManagementApi(MetadataReader md)
    {
        foreach (var h in md.TypeReferences)
        {
            var tr = md.GetTypeReference(h);
            var depth = 0;
            while (tr.ResolutionScope.Kind == HandleKind.TypeReference && depth++ < 64)
                tr = md.GetTypeReference((TypeReferenceHandle)tr.ResolutionScope);
            if (md.GetString(tr.Namespace).StartsWith("Umbraco.Cms.Api.Management", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
