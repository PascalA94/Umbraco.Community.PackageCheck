using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PackageCheck.Tests.Support;

/// <summary>
/// Builds synthetic nupkgs (zip files) for inspector tests. Real packages are never committed (goldens download and
/// verify by hash). Each build writes to its own file under a dedicated temp folder, so tests can check that the
/// inspector writes nothing next to it.
/// </summary>
public sealed class NupkgBuilder
{
    private readonly string _id;
    private readonly string _version;
    private readonly List<(string Name, byte[] Content, long? DeclaredSize)> _entries = [];
    private string? _nuspec;

    private NupkgBuilder(string id, string version) { _id = id; _version = version; }

    public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "packagecheck-nupkg-tests");

    public static NupkgBuilder Create(string id, string version) => new(id, version);

    /// <summary>A nupkg holding one DLL under <c>lib/net10.0/</c>, as the type-scan tests use.</summary>
    public static string WithDll(string dllPath, string id = "Stub.Package", string version = "1.0.0") =>
        Create(id, version).WithFile($"lib/net10.0/{Path.GetFileName(dllPath)}", File.ReadAllBytes(dllPath)).Build();

    public NupkgBuilder WithNuspec(string nuspecXml) { _nuspec = nuspecXml; return this; }

    public NupkgBuilder WithFile(string path, byte[] content) { _entries.Add((path, content, null)); return this; }

    public NupkgBuilder WithFile(string path, string content) => WithFile(path, Encoding.UTF8.GetBytes(content));

    /// <summary>An entry whose name is written as-is (zip-slip and rooted names); content is one byte.</summary>
    public NupkgBuilder WithRawEntryName(string name) { _entries.Add((name, [0x41], null)); return this; }

    /// <summary>An entry whose local and central-directory "uncompressed size" fields claim <paramref name="declaredSize"/>.</summary>
    public NupkgBuilder WithLyingSize(string path, byte[] content, long declaredSize)
    {
        _entries.Add((path, content, declaredSize));
        return this;
    }

    public string Build()
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"{_id}.{_version}.{Guid.NewGuid():N}.nupkg");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var nuspec = _nuspec ?? $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata><id>{_id}</id><version>{_version}</version><authors>test</authors><description>test</description></metadata>
                </package>
                """;
            Write(zip, $"{_id}.nuspec", Encoding.UTF8.GetBytes(nuspec));
            foreach (var (name, content, _) in _entries) Write(zip, name, content);
        }

        foreach (var (name, _, declared) in _entries.Where(e => e.DeclaredSize is not null))
            PatchUncompressedSize(path, name, (uint)declared!.Value);
        return path;
    }

    private static void Write(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(content);
    }

    // Rewrites the "uncompressed size" of one entry in both the local file header (offset 22) and the central
    // directory header (offset 24), the way a hostile archive would lie about how far an entry inflates.
    private static void PatchUncompressedSize(string path, string entryName, uint size)
    {
        var bytes = File.ReadAllBytes(path);
        var name = Encoding.UTF8.GetBytes(entryName);
        var patched = 0;
        for (var i = 0; i + 46 < bytes.Length; i++)
        {
            var sig = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i));
            if (sig == 0x04034b50 && NameAt(bytes, i + 30, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 26)), name))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 22), size);
                patched++;
            }
            else if (sig == 0x02014b50 && NameAt(bytes, i + 46, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 28)), name))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24), size);
                patched++;
            }
        }
        if (patched != 2) throw new InvalidOperationException($"expected 2 headers for {entryName}, patched {patched}");
        File.WriteAllBytes(path, bytes);
    }

    private static bool NameAt(byte[] bytes, int offset, int length, byte[] name) =>
        length == name.Length && offset + length <= bytes.Length && bytes.AsSpan(offset, length).SequenceEqual(name);
}
