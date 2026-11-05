using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Inspect;

/// <summary>
/// The bytes one nupkg may inflate to across every entry read (plan review P7: 500 MB by default, the architect's call).
/// One instance per inspection, shared by every <see cref="CountingReadStream"/> it opens.
/// </summary>
public sealed class ByteBudget(long limit)
{
    private long _spent;

    public void Spend(long bytes)
    {
        _spent += bytes;
        if (_spent > limit) throw new PackageSourceException(UnknownReason.UnsafeArchive, "archive exceeded total size cap");
    }
}

/// <summary>
/// Read-only wrapper that counts bytes actually read. A zip's central directory can lie about an entry's size, so the
/// per-entry cap and the archive budget are enforced here on real bytes, never on <c>ZipArchiveEntry.Length</c>
/// (design 4, plan review P7).
/// </summary>
public sealed class CountingReadStream(Stream inner, long entryLimit, ByteBudget total) : Stream
{
    private long _read;

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Count(int n)
    {
        _read += n;
        if (_read > entryLimit) throw new PackageSourceException(UnknownReason.UnsafeArchive, "entry exceeded its size cap");
        total.Spend(n);
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
