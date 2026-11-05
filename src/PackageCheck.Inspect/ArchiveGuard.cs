using System.Buffers.Binary;
using System.IO.Compression;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Inspect;

/// <summary>
/// Checks run over the whole central directory before any entry is opened (design 4): the entry count cap and the
/// zip-slip / rooted-path rules. Nothing is ever extracted, but a name that would escape a folder marks the archive as
/// hostile, so the package becomes <c>unknown</c> / <c>unsafe-archive</c> rather than being judged on its contents.
/// </summary>
public static class ArchiveGuard
{
    public static void Check(ZipArchive zip, ArchiveLimits l)
    {
        if (zip.Entries.Count > l.MaxEntries)
            throw new PackageSourceException(UnknownReason.UnsafeArchive, $"archive has more than {l.MaxEntries} entries");
        foreach (var e in zip.Entries)
        {
            if (IsUnsafeName(e.FullName))
                throw new PackageSourceException(UnknownReason.UnsafeArchive, "archive has an entry name that escapes its folder");
        }
    }

    // Bytes per central-directory record we accept on average: the 46-byte fixed header plus a generous name and extra field.
    private const long MaxRecordBytes = 46 + 1024;

    /// <summary>
    /// Reads the end-of-central-directory record (and its zip64 form) before <see cref="ZipArchive"/> is built, because
    /// ZipArchive loads every central-directory record into memory before anyone can count them (security review M3:
    /// 5 million records under the 250 MB cap cost 1.5 GB). A declared entry count or directory size past the caps is
    /// refused up front. A missing or unreadable record is left for ZipArchive to reject. The stream is rewound.
    /// </summary>
    public static void PreCheck(Stream file, ArchiveLimits l)
    {
        var length = file.Length;
        var tailLength = (int)Math.Min(length, 22 + ushort.MaxValue + 20);
        var tail = new byte[tailLength];
        file.Position = length - tailLength;
        file.ReadExactly(tail);
        file.Position = 0;

        var eocd = -1;
        for (var i = tailLength - 22; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) return;

        ulong entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 10));
        ulong size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12));
        if ((entries == ushort.MaxValue || size == uint.MaxValue) && eocd >= 20
            && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd - 20)) == 0x07064b50)
        {
            var eocd64At = BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(eocd - 20 + 8));
            if (eocd64At > (ulong)(length - 56))
                throw new PackageSourceException(UnknownReason.UnsafeArchive, "archive has an invalid zip64 directory");
            var eocd64 = new byte[56];
            file.Position = (long)eocd64At;
            file.ReadExactly(eocd64);
            file.Position = 0;
            if (BinaryPrimitives.ReadUInt32LittleEndian(eocd64) != 0x06064b50)
                throw new PackageSourceException(UnknownReason.UnsafeArchive, "archive has an invalid zip64 directory");
            entries = BinaryPrimitives.ReadUInt64LittleEndian(eocd64.AsSpan(32));
            size = BinaryPrimitives.ReadUInt64LittleEndian(eocd64.AsSpan(40));
        }

        if (entries > (ulong)l.MaxEntries)
            throw new PackageSourceException(UnknownReason.UnsafeArchive, $"archive has more than {l.MaxEntries} entries");
        if (size > (ulong)(l.MaxEntries * MaxRecordBytes))
            throw new PackageSourceException(UnknownReason.UnsafeArchive, "archive central directory is larger than its entry cap allows");
    }

    // The entry name itself is package-controlled, so it is left out of the message; the reason is enough.
    public static bool IsUnsafeName(string name) =>
        name.StartsWith('/') || name.StartsWith('\\')
        || (name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':')
        || name.Split('/', '\\').Contains("..");
}
