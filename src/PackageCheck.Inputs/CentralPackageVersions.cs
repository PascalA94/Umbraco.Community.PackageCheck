using System.Xml.Linq;

namespace PackageCheck.Inputs;

public sealed record GlobalReference(string Id, string VersionText);

/// <summary>
/// Versions from the nearest Directory.Packages.props above a project (NuGet central package management),
/// following Import of a parent props file. Read-only: nothing is evaluated beyond $(Name) substitution.
/// </summary>
public sealed class CentralPackageVersions
{
    private const int MaxImportDepth = 8;
    private const string FileName = "Directory.Packages.props";

    private readonly Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GlobalReference> globals = [];
    private readonly Dictionary<string, string> conflicts = new(StringComparer.OrdinalIgnoreCase);

    private CentralPackageVersions()
    {
    }

    /// <summary>Set when the props file could not be read: every central version is then unknown.</summary>
    public string? Warning { get; private set; }

    public IReadOnlyList<GlobalReference> GlobalReferences => globals;

    /// <summary>
    /// Null when no Directory.Packages.props exists above the project or central management is switched off
    /// (ManagePackageVersionsCentrally not true). A props file that cannot be read still returns an instance,
    /// carrying <see cref="Warning"/>, so the caller can mark affected packages unresolved instead of guessing.
    /// </summary>
    public static CentralPackageVersions? ForProject(string csprojPath, MsBuildProperties props, string? stopAt = null)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
        var file = FindAbove(projectDir, stopAt);
        if (file is null)
        {
            return null;
        }

        var result = new CentralPackageVersions();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var overlay = props;
        var ownFlag = (string?)null;
        if (!result.Load(file, stopAt, ref overlay, visited, 0, isRoot: true, ref ownFlag))
        {
            // Unreadable: honour an explicit opt-out from elsewhere, otherwise assume central management is on.
            return string.Equals(props.Get("ManagePackageVersionsCentrally"), "false", StringComparison.OrdinalIgnoreCase)
                ? null
                : result;
        }

        var flag = props.Get("ManagePackageVersionsCentrally") ?? ownFlag;
        return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase) ? result : null;
    }

    /// <summary>The version text for a package id, or null when the id has no central entry.</summary>
    public string? VersionFor(string id) => conflicts.ContainsKey(id) ? null : versions.TryGetValue(id, out var v) ? v : null;

    /// <summary>
    /// When one id has several PackageVersion entries in a file, at least one conditioned, with different versions,
    /// the tool cannot tell which applies (conditions are not evaluated). Returns the raw texts joined, else null.
    /// </summary>
    public string? ConflictFor(string id) => conflicts.TryGetValue(id, out var v) ? v : null;

    private bool Load(string file, string? stopAt, ref MsBuildProperties overlay, HashSet<string> visited, int depth, bool isRoot, ref string? ownFlag)
    {
        var full = Path.GetFullPath(file);
        if (depth > MaxImportDepth || !visited.Add(full))
        {
            return true;
        }

        XDocument doc;
        try
        {
            doc = XmlInput.Load(full);
        }
        catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            // Exception text is not echoed: it can quote file content.
            Warning ??= $"{Path.GetFileName(Path.GetDirectoryName(full))}/{FileName}: could not be read; central package versions are unknown";
            return false;
        }

        var dir = Path.GetDirectoryName(full)!;

        // Parent first, so entries in this file override the ones it imports.
        foreach (var import in XmlInput.Elements(doc, "Import"))
        {
            var target = ResolveImport(XmlInput.Attr(import, "Project"), dir, stopAt);
            if (target is not null && !Load(target, stopAt, ref overlay, visited, depth + 1, isRoot: false, ref ownFlag) && Warning is not null)
            {
                return false;
            }
        }

        overlay = overlay.With(doc);
        if (isRoot)
        {
            ownFlag = overlay.Get("ManagePackageVersionsCentrally");
        }

        var entries = new Dictionary<string, List<(string Text, bool Conditioned)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in XmlInput.Elements(doc, "PackageVersion"))
        {
            var id = XmlInput.Attr(item, "Include") ?? XmlInput.Attr(item, "Update");
            var text = XmlInput.AttrOrChild(item, "Version");
            if (!string.IsNullOrWhiteSpace(id) && text is not null)
            {
                // Keep the raw text when it cannot be expanded here; the caller then reports it as unresolved.
                if (!entries.TryGetValue(id, out var list))
                {
                    entries[id] = list = [];
                }

                list.Add((overlay.Expand(text) ?? text, XmlInput.IsConditioned(item)));
            }
        }

        foreach (var (id, list) in entries)
        {
            // A nearer file replaces whatever a parent said, including a parent's conflict.
            conflicts.Remove(id);
            versions[id] = list[^1].Text;
            var distinct = list.Select(e => e.Text).Distinct(StringComparer.Ordinal).ToList();
            if (list.Any(e => e.Conditioned) && distinct.Count > 1)
            {
                conflicts[id] = string.Join("; ", distinct);
            }
        }

        foreach (var item in XmlInput.Elements(doc, "GlobalPackageReference"))
        {
            var id = XmlInput.Attr(item, "Include");
            var text = XmlInput.AttrOrChild(item, "Version");
            if (!string.IsNullOrWhiteSpace(id) && text is not null)
            {
                globals.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                globals.Add(new GlobalReference(id, overlay.Expand(text) ?? text));
            }
        }

        return true;
    }

    private static string? ResolveImport(string? project, string currentDir, string? stopAt)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return null;
        }

        // The documented pattern: import the next Directory.Packages.props above this one.
        if (project.Contains("GetPathOfFileAbove", StringComparison.OrdinalIgnoreCase)
            && project.Contains(FileName, StringComparison.OrdinalIgnoreCase))
        {
            // The search must not climb past the boundary the caller set (or the repo root).
            var atBoundary = XmlInput.Ancestors(currentDir, stopAt).Count() == 1;
            var parent = Path.GetDirectoryName(currentDir);
            return atBoundary || string.IsNullOrEmpty(parent) ? null : FindAbove(parent, stopAt);
        }

        var literal = project.Replace("$(MSBuildThisFileDirectory)", currentDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (literal.Contains("$(", StringComparison.Ordinal))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(currentDir, literal.Replace('\\', '/')));
        return File.Exists(path) ? path : null;
    }

    private static string? FindAbove(string startDir, string? stopAt)
    {
        foreach (var dir in XmlInput.Ancestors(startDir, stopAt))
        {
            var candidate = Path.Combine(dir, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
