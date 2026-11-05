namespace PackageCheck.Cli.Commands;

/// <summary>
/// The scan paths as sites: full paths, trailing separators trimmed, duplicates dropped in first-seen order. A
/// duplicate is scanned once, with no warning (plan review P6). Case is ignored where the file system ignores it.
/// </summary>
public static class PathSet
{
    private static readonly StringComparer Comparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static IReadOnlyList<string> Normalise(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(Comparer);
        var result = new List<string>();
        foreach (var p in paths)
        {
            var full = Path.GetFullPath(p);
            var root = Path.GetPathRoot(full) ?? "";
            if (full.Length > root.Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (seen.Add(full)) result.Add(full);
        }
        return result;
    }
}
