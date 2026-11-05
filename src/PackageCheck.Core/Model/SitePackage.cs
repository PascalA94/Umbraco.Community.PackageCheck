namespace PackageCheck.Core.Model;

/// <summary>
/// One distinct package (id, version) in a site and every project that uses it. <paramref name="Projects"/> are names
/// for reports; <paramref name="ProjectDirectories"/> are those projects' folders, read only, for H17.
/// </summary>
public sealed record SitePackage(PackageRef Ref, IReadOnlyList<string> Projects, IReadOnlyList<string>? ProjectDirectories = null);

public static class SitePackages
{
    /// <summary>
    /// Review Focus 3: each distinct (id case-insensitive, resolved version or raw version text) is one row, so a
    /// test project on an old version stays visible and a shared version is not judged twice.
    /// Project names are file names without extension, sorted ordinally.
    /// </summary>
    public static IReadOnlyList<SitePackage> From(ScanInput input)
    {
        var rows = new Dictionary<(string Id, string Version), (PackageRef Ref, SortedSet<string> Projects, SortedSet<string> Dirs)>(
            new KeyComparer());
        var order = new List<(string, string)>();

        foreach (var project in input.Projects)
        {
            var name = Path.GetFileNameWithoutExtension(project.Path);
            var dir = Path.GetDirectoryName(Path.GetFullPath(project.Path)) ?? "";
            foreach (var p in project.Packages)
            {
                var key = (p.Id, p.Version?.ToNormalizedString() ?? $"raw:{p.VersionText}|{p.UnresolvedReason}");
                if (rows.TryGetValue(key, out var row))
                {
                    row.Projects.Add(name);
                    row.Dirs.Add(dir);
                    // A direct reference anywhere makes the row direct: that is the stricter reading for the rules.
                    if (p.Direct && !row.Ref.Direct)
                    {
                        rows[key] = (row.Ref with { Direct = true }, row.Projects, row.Dirs);
                    }
                }
                else
                {
                    rows[key] = (p, new SortedSet<string>(StringComparer.Ordinal) { name }, new SortedSet<string>(StringComparer.Ordinal) { dir });
                    order.Add(key);
                }
            }
        }

        return [.. order.Select(k => new SitePackage(rows[k].Ref, [.. rows[k].Projects], [.. rows[k].Dirs]))];
    }

    private sealed class KeyComparer : IEqualityComparer<(string Id, string Version)>
    {
        public bool Equals((string Id, string Version) x, (string Id, string Version) y) =>
            string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase) && x.Version == y.Version;

        public int GetHashCode((string Id, string Version) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Id), k.Version);
    }
}
