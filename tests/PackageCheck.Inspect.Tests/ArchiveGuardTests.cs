using System.IO.Compression;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Tests.Support;

namespace PackageCheck.Inspect.Tests;

public class ArchiveGuardTests
{
    private static void Check(string path, ArchiveLimits? limits = null)
    {
        using var zip = ZipFile.OpenRead(path);
        ArchiveGuard.Check(zip, limits ?? ArchiveLimits.Default);
    }

    [Theory]
    [InlineData("lib/net10.0/A.dll")]
    [InlineData("staticwebassets/App_Plugins/A/a..b.js")] // ".." inside a name is not a segment
    [InlineData("content/..hidden/x")]
    public void SafeNames_Pass(string name) => Check(NupkgBuilder.Create("A", "1.0.0").WithRawEntryName(name).Build());

    [Theory]
    [InlineData("..")]
    [InlineData("a/../../b")]
    [InlineData("a\\..\\b")]
    [InlineData("/abs")]
    [InlineData("\\abs")]
    [InlineData("D:x")]
    public void UnsafeNames_Throw(string name)
    {
        var ex = Assert.Throws<PackageSourceException>(() => Check(NupkgBuilder.Create("A", "1.0.0").WithRawEntryName(name).Build()));
        Assert.Equal(UnknownReason.UnsafeArchive, ex.Reason);
    }

    [Fact]
    public void TooManyEntries_Throws()
    {
        var b = NupkgBuilder.Create("A", "1.0.0").WithFile("a", "x").WithFile("b", "x");
        var ex = Assert.Throws<PackageSourceException>(() => Check(b.Build(), ArchiveLimits.Default with { MaxEntries = 2 }));
        Assert.Equal(UnknownReason.UnsafeArchive, ex.Reason);
    }

    // Security review M3 (S4): ZipArchive reads the whole central directory before any count check (5M records cost
    // 1.5 GB). The end-of-central-directory record is read first and an oversized declaration is refused up front.
    [Fact]
    public void DeclaredEntryCountOverCap_RejectedBeforeZipArchive()
    {
        var path = NupkgBuilder.Create("A", "1.0.0").WithFile("a", "x").Build();
        var bytes = File.ReadAllBytes(path);
        var eocd = LastIndexOf(bytes, 0x06054b50);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 8), 60000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 10), 60000);
        using var s = new MemoryStream(bytes);
        var ex = Assert.Throws<PackageSourceException>(() => ArchiveGuard.PreCheck(s, ArchiveLimits.Default));
        Assert.Equal(UnknownReason.UnsafeArchive, ex.Reason);
        Assert.Contains("entries", ex.Message);
    }

    [Fact]
    public void DeclaredCentralDirectoryOverBound_Rejected()
    {
        var path = NupkgBuilder.Create("A", "1.0.0").WithFile("a", "x").Build();
        var bytes = File.ReadAllBytes(path);
        var eocd = LastIndexOf(bytes, 0x06054b50);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(eocd + 12), 0xFFFFFFF0);
        using var s = new MemoryStream(bytes);
        var ex = Assert.Throws<PackageSourceException>(() => ArchiveGuard.PreCheck(s, ArchiveLimits.Default with { MaxEntries = 10 }));
        Assert.Contains("central directory", ex.Message);
    }

    [Fact]
    public void NormalArchive_PassesPreCheck_AndRewinds()
    {
        using var s = File.OpenRead(NupkgBuilder.Create("A", "1.0.0").WithFile("a", "x").Build());
        ArchiveGuard.PreCheck(s, ArchiveLimits.Default);
        Assert.Equal(0, s.Position);
    }

    private static int LastIndexOf(byte[] b, uint sig)
    {
        for (var i = b.Length - 22; i >= 0; i--)
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i)) == sig) return i;
        throw new InvalidOperationException("no EOCD");
    }
}

