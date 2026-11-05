using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Inspect.Tests;

public class CountingReadStreamTests
{
    private static void Drain(Stream s)
    {
        var buf = new byte[7];
        while (s.Read(buf, 0, buf.Length) > 0) { }
    }

    [Fact]
    public void CountingReadStream_ExactlyAtLimit_Ok()
    {
        Drain(new CountingReadStream(new MemoryStream(new byte[100]), 100, new ByteBudget(1000)));

        var ex = Assert.Throws<PackageSourceException>(() =>
            Drain(new CountingReadStream(new MemoryStream(new byte[101]), 100, new ByteBudget(1000))));
        Assert.Equal(UnknownReason.UnsafeArchive, ex.Reason);
        Assert.Contains("entry exceeded its size cap", ex.Message);
    }

    [Fact]
    public void SharedBudget_SpansEntries()
    {
        var budget = new ByteBudget(150);
        Drain(new CountingReadStream(new MemoryStream(new byte[100]), 100, budget));
        var ex = Assert.Throws<PackageSourceException>(() => Drain(new CountingReadStream(new MemoryStream(new byte[60]), 100, budget)));
        Assert.Equal(UnknownReason.UnsafeArchive, ex.Reason);
        Assert.Contains("archive exceeded total size cap", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_IsCountedToo()
    {
        var s = new CountingReadStream(new MemoryStream(new byte[101]), 100, new ByteBudget(1000));
        await Assert.ThrowsAsync<PackageSourceException>(() => s.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void IsReadOnly()
    {
        var s = new CountingReadStream(new MemoryStream(new byte[1]), 10, new ByteBudget(10));
        Assert.True(s.CanRead);
        Assert.False(s.CanWrite);
        Assert.False(s.CanSeek);
        Assert.Throws<NotSupportedException>(() => { s.Write(new byte[1], 0, 1); });
    }
}
