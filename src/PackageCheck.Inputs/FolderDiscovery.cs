using PackageCheck.Core;

namespace PackageCheck.Inputs;

public enum EntryKind { Slnx, Sln, Csproj, ProjectSet }

/// <summary>Where a scan starts. For <see cref="EntryKind.ProjectSet"/> the path is the folder itself.</summary>
public sealed record EntryPoint(EntryKind Kind, string Path);

/// <summary>Design 6.1 "Path handling": .slnx, then .sln, then *.csproj, four levels deep, skipping build and VCS folders.</summary>
public static class FolderDiscovery
{
    // Folders below the start folder; the start folder's own files are level 0.
    private const int MaxDepth = 4;

    private static readonly HashSet<string> SkippedFolders =
        new(["bin", "obj", "node_modules", ".git"], StringComparer.OrdinalIgnoreCase);

    public static EntryPoint FindEntry(string path)
    {
        if (File.Exists(path))
        {
            var full = System.IO.Path.GetFullPath(path);
            return System.IO.Path.GetExtension(full).ToLowerInvariant() switch
            {
                ".slnx" => new EntryPoint(EntryKind.Slnx, full),
                ".sln" => new EntryPoint(EntryKind.Sln, full),
                ".csproj" => new EntryPoint(EntryKind.Csproj, full),
                _ => throw new NoUmbracoProjectException(path),
            };
        }

        if (!Directory.Exists(path))
        {
            throw new PathNotFoundException(path);
        }

        var root = System.IO.Path.GetFullPath(path);
        var found = new List<(int Depth, string Path)>();
        Walk(root, 0, found);

        // Kind priority beats depth: a solution file is the author's statement of what belongs to the site.
        foreach (var (kind, ext) in new[] { (EntryKind.Slnx, ".slnx"), (EntryKind.Sln, ".sln") })
        {
            var hit = Pick(found, ext);
            if (hit is not null)
            {
                return new EntryPoint(kind, hit);
            }
        }

        return Pick(found, ".csproj") is not null
            ? new EntryPoint(EntryKind.ProjectSet, root)
            : throw new NoUmbracoProjectException(path);
    }

    /// <summary>Every .csproj under <paramref name="root"/> within the discovery depth, in a stable order.</summary>
    public static IReadOnlyList<string> FindProjects(string root)
    {
        var found = new List<(int Depth, string Path)>();
        Walk(System.IO.Path.GetFullPath(root), 0, found);
        return [.. found.Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => f.Path)];
    }

    // Shallowest first, then ordinal, so the choice never depends on file system enumeration order.
    private static string? Pick(List<(int Depth, string Path)> found, string extension) =>
        found.Where(f => f.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Depth)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => f.Path)
            .FirstOrDefault();

    private static void Walk(string dir, int depth, List<(int, string)> found)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = System.IO.Path.GetExtension(file);
                if (ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add((depth, file));
                }
            }

            if (depth == MaxDepth)
            {
                return;
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var info = new DirectoryInfo(sub);
                // Links are not followed: a loop would otherwise burn the depth budget on repeats.
                if (SkippedFolders.Contains(info.Name) || info.LinkTarget is not null)
                {
                    continue;
                }

                Walk(sub, depth + 1, found);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            // An unreadable folder is treated as empty; the scan must not fail on it.
        }
    }
}
