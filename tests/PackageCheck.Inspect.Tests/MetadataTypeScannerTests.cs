using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Tests.Support;

namespace PackageCheck.Inspect.Tests;

public class MetadataTypeScannerTests
{
    private static readonly PackageIdentity Id = new("Stub.Package", NuGetVersion.Parse("1.0.0"));

    private static string StubPath(string name) => Path.Combine(AppContext.BaseDirectory, "StubAssemblies", name);

    private static TypeRefScan Scan(string stub)
    {
        using var s = File.OpenRead(StubPath(stub));
        return MetadataTypeScanner.Scan(s);
    }

    private static Task<InspectionResult> InspectNupkg(string path) =>
        new NupkgInspector(ArchiveLimits.Default).InspectAsync(Id, new NupkgHandle(path, false, new FileInfo(path).Length), TestContext.Current.CancellationToken);

    [Fact]
    public void Stub_ManifestReader_Detected() => Assert.Equal(new TypeRefScan(true, false), Scan("ManifestReaderStub.dll"));

    [Fact]
    public void Stub_ManagementApi_Detected() => Assert.Equal(new TypeRefScan(false, true), Scan("ManagementApiStub.dll"));

    [Fact]
    public void Plain_NothingDetected() => Assert.Equal(new TypeRefScan(false, false), Scan("PlainStub.dll"));

    [Fact]
    public void ReferenceWithoutImplementation_IsNotAManifestReader() =>
        // Ruling R23: only a type implementing the interface counts; the PackageManifest leg is dropped.
        Assert.Equal(new TypeRefScan(false, false), Scan("PackageManifestOnlyStub.dll"));

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x4D, 0x5A, 0, 0, 1, 2, 3 })]
    public void NotAPeFile_ReturnsFalse_NoThrow(byte[] bytes) =>
        Assert.Equal(new TypeRefScan(false, false), MetadataTypeScanner.Scan(new MemoryStream(bytes)));

    [Fact]
    public void TruncatedPe_ReturnsFalse_NoThrow()
    {
        var bytes = File.ReadAllBytes(StubPath("ManifestReaderStub.dll"));
        Assert.Equal(new TypeRefScan(false, false), MetadataTypeScanner.Scan(new MemoryStream(bytes[..(bytes.Length / 2)])));
    }

    [Fact]
    public async Task AssemblyNeverLoaded()
    {
        var r = await InspectNupkg(NupkgBuilder.WithDll(StubPath("ManifestReaderStub.dll")));
        Assert.True(r.ReferencesManifestReader);
        // Name check, not a before/after diff: other tests in the run may load unrelated assemblies (plan review P14).
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "ManifestReaderStub");
    }

    [Fact]
    public async Task Inspector_ScansEveryLibFolder_PerTfm()
    {
        var path = NupkgBuilder.Create("A", "1.0.0")
            .WithFile("lib/net10.0/A.dll", File.ReadAllBytes(StubPath("ManifestReaderStub.dll")))
            .WithFile("lib/net8.0/A.dll", File.ReadAllBytes(StubPath("ManagementApiStub.dll")))
            .WithFile("lib/net8.0/sub/Ignored.dll", File.ReadAllBytes(StubPath("ManagementApiStub.dll")))
            .Build();
        var r = await InspectNupkg(path);
        Assert.True(r.ReferencesManifestReader);
        Assert.True(r.ReferencesManagementApi);
        Assert.Equal(
            [new LibScan("net10.0", true, false), new LibScan("net8.0", false, true)],
            r.LibScans!.OrderBy(x => x.Tfm, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Inspector_DllOverEntryCap_SkippedWithNote()
    {
        var path = NupkgBuilder.WithDll(StubPath("ManifestReaderStub.dll"));
        var r = await new NupkgInspector(ArchiveLimits.Default with { MaxEntryBytes = 100 })
            .InspectAsync(Id, new NupkgHandle(path, false, new FileInfo(path).Length), TestContext.Current.CancellationToken);
        Assert.False(r.ReferencesManifestReader);
        Assert.Contains(r.Notes!, n => n.Contains("ManifestReaderStub.dll") && n.Contains("size cap"));
    }

    [Fact]
    public void InspectAssembly_HasNoLoadApis()
    {
        // Reads PackageCheck.Inspect's own IL metadata: no member reference to an assembly-loading API, no reference to
        // MetadataLoadContext. The banned-API analyzer stops these at build time; this proves the shipped binary.
        using var pe = new PEReader(File.OpenRead(typeof(MetadataTypeScanner).Assembly.Location));
        var md = pe.GetMetadataReader();
        var calls = md.MemberReferences.Select(h => md.GetMemberReference(h)).Select(m => (Parent: ParentName(md, m.Parent), Name: md.GetString(m.Name))).ToList();
        var types = md.TypeReferences.Select(h => md.GetTypeReference(h)).Select(t => $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}").ToList();

        Assert.NotEmpty(calls); // the reader really saw member references
        Assert.DoesNotContain(calls, c => c.Parent == "System.Reflection.Assembly" && c.Name.StartsWith("Load", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, c => c.Parent == "System.Runtime.Loader.AssemblyLoadContext" && c.Name.StartsWith("Load", StringComparison.Ordinal));
        Assert.DoesNotContain(types, t => t == "System.Reflection.MetadataLoadContext");
        Assert.DoesNotContain(types, t => t == "System.Runtime.Loader.AssemblyLoadContext");
    }

    private static string ParentName(MetadataReader md, EntityHandle parent)
    {
        if (parent.Kind != HandleKind.TypeReference) return "";
        var t = md.GetTypeReference((TypeReferenceHandle)parent);
        return $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}";
    }
}

public class MetadataTypeScannerFuzzTests
{
    // Security review M3 (S2): mutated DLLs made System.Reflection.Metadata throw OverflowException, which escaped Scan
    // and aborted the whole scan. Any malformed DLL must read as "no evidence", never as an exception.
    [Fact]
    public void MutatedDlls_NeverThrow()
    {
        var seed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "StubAssemblies", "ManifestReaderStub.dll"));
        var rng = new Random(20261105);
        for (var i = 0; i < 100_000; i++)
        {
            var b = (byte[])seed.Clone();
            for (int m = 0, n = rng.Next(1, 20); m < n; m++)
            {
                var pos = rng.Next(b.Length);
                b[pos] = rng.Next(3) switch { 0 => (byte)rng.Next(256), 1 => 0xFF, _ => 0 };
            }
            if (rng.Next(10) == 0) Array.Resize(ref b, rng.Next(64, b.Length));
            var ex = Record.Exception(() => MetadataTypeScanner.Scan(new MemoryStream(b)));
            Assert.True(ex is null, $"iteration {i}: {ex?.GetType().Name}");
        }
    }
}
