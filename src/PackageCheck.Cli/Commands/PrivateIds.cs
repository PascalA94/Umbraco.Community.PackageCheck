using PackageCheck.Core;

namespace PackageCheck.Cli.Commands;

/// <summary>
/// <c>--private-ids &lt;file&gt;</c> (design 7.3): ids that live on a private feed. Each becomes <c>unknown</c>,
/// <c>private-feed</c>, and is never sent to nuget.org or the Marketplace. One id per line; <c>#</c> starts a comment.
/// </summary>
public static class PrivateIds
{
    public static IReadOnlySet<string> Load(string file)
    {
        if (!File.Exists(file)) throw new UsageException($"--private-ids file not found: {file}");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(file))
        {
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            var id = (hash < 0 ? line : line[..hash]).Trim();
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }
}
