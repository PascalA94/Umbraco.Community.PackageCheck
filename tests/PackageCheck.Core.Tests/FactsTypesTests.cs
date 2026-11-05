using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Core.Tests;

public class FactsTypesTests
{
    [Fact]
    public async Task NupkgHandle_Temporary_DeletesFileOnDispose()
    {
        var path = Path.GetTempFileName();
        await using (new NupkgHandle(path, IsTemporary: true, Length: 0)) { }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task NupkgHandle_NotTemporary_LeavesFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using (new NupkgHandle(path, IsTemporary: false, Length: 0)) { }
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NupkgHandle_Temporary_AlreadyGone_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await new NupkgHandle(path, IsTemporary: true, Length: 0).DisposeAsync();
    }

    [Fact]
    public void ArchiveLimits_Default_MatchesDesignCaps()
    {
        var l = ArchiveLimits.Default;
        Assert.Equal(250L * 1024 * 1024, l.MaxNupkgBytes);
        Assert.Equal(20_000, l.MaxEntries);
        Assert.Equal(50L * 1024 * 1024, l.MaxEntryBytes);
        Assert.Equal(500L * 1024 * 1024, l.MaxTotalBytes);
    }

    [Theory]
    [InlineData(UnknownReason.Offline, "offline")]
    [InlineData(UnknownReason.UnresolvedProperty, "unresolved-property")]
    [InlineData(UnknownReason.PrivateFeed, "private-feed")]
    [InlineData(UnknownReason.NotFound, "not-found")]
    [InlineData(UnknownReason.UnsafeArchive, "unsafe-archive")]
    [InlineData(UnknownReason.ConflictingEvidence, "conflicting-evidence")]
    [InlineData(UnknownReason.SourceError, "source-error")]
    public void UnknownReason_WireValues(UnknownReason reason, string wire) => Assert.Equal(wire, reason.ToWireValue());

    [Fact]
    public void PackageSourceException_KeepsReasonAndNoInner()
    {
        var e = new PackageSourceException(UnknownReason.PrivateFeed, "feed refused");
        Assert.Equal(UnknownReason.PrivateFeed, e.Reason);
        Assert.Null(e.InnerException);
    }
}
