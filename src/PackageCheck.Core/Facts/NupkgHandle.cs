namespace PackageCheck.Core.Facts;

/// <summary>
/// A nupkg on disk. Deliberate deviation from the design 7.2 sketch's <c>Stream</c>: the inspector needs a seekable
/// file for <c>ZipArchive</c>, and the handle owns deletion of a temporary download (design 7.5 step 3).
/// Global-packages-folder files are never temporary and are never deleted.
/// </summary>
public sealed record NupkgHandle(string FilePath, bool IsTemporary, long Length) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        if (IsTemporary)
        {
            // File.Delete is a no-op for a missing file. A file still locked by a reader (Windows) is left behind
            // rather than failing the scan; it sits in the tool's own temp folder, never in the scanned site.
            try { File.Delete(FilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return ValueTask.CompletedTask;
    }
}
