namespace PackageCheck.Core.Facts;

/// <summary>Read-only view of a scanned project's folders (H17). The tool never writes to a scanned folder.</summary>
public interface IProjectFileSystem
{
    /// <summary>Folders directly under <c>App_Plugins/</c> and <c>wwwroot/App_Plugins/</c>, as "App_Plugins/Name" paths.</summary>
    IReadOnlyList<string> AppPluginsFolders(string projectDir);
}

public sealed class ProjectFileSystem : IProjectFileSystem
{
    private static readonly string[] Roots = ["App_Plugins", "wwwroot/App_Plugins"];

    public IReadOnlyList<string> AppPluginsFolders(string projectDir)
    {
        var found = new List<string>();
        foreach (var root in Roots)
        {
            var dir = Path.Combine(projectDir, root.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (!Directory.Exists(dir)) continue;
                found.AddRange(Directory.EnumerateDirectories(dir).Select(d => $"{root}/{PackageText.Safe(Path.GetFileName(d))}"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unreadable folder is no evidence; H17 is a warning only and must never fail a scan.
            }
        }
        return found;
    }
}
