using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace PackageCheck.Inputs;

public sealed record SolutionRead(IReadOnlyList<string> ProjectPaths, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads the project files listed in a .sln or .slnx. Decision: spike S4 (SolutionPersistence, GO).
/// Never throws for a bad solution: a solution we cannot read is a warning and no projects.
/// </summary>
public static class SolutionFileReader
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    public static SolutionRead Read(string solutionPath)
    {
        var warnings = new List<string>();
        var name = Path.GetFileName(solutionPath);
        try
        {
            var full = Path.GetFullPath(solutionPath);
            var dir = Path.GetDirectoryName(full)!;
            var listed = ReadListedPaths(full, name, warnings);
            return new SolutionRead(Resolve(listed, dir, warnings), warnings);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Exception text is not echoed: it can carry file content. The file name is enough to act on.
            return new SolutionRead([], [$"could not read solution {name}: {e.GetType().Name}; skipped"]);
        }
    }

    private static List<string> ReadListedPaths(string full, string name, List<string> warnings)
    {
        var isSlnx = full.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
        List<string>? fromLibrary = null;
        try
        {
            var serializer = SolutionSerializers.GetSerializerByMoniker(full)
                ?? throw new NotSupportedException("unsupported solution type");
            var model = serializer.OpenAsync(full, CancellationToken.None).GetAwaiter().GetResult();
            fromLibrary = model.SolutionProjects.Select(p => p.FilePath).ToList();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The library is stricter than the tools that wrote the file (for example it refuses one
            // project listed twice in one folder). A strict refusal must not hide every project.
        }

        if (isSlnx)
        {
            List<string>? fromXml = null;
            try
            {
                fromXml = ReadSlnxProjectAttributes(full);
            }
            catch (Exception e) when (e is XmlException or IOException)
            {
                // Handled below: the library result alone, or neither.
            }

            if (fromLibrary is not null && fromXml is not null)
            {
                return ReconcileSlnx(fromLibrary, fromXml, name, warnings);
            }

            if (fromXml is not null)
            {
                warnings.Add($"{name}: solution reader could not parse the file; using the project list read directly from it");
                return fromXml;
            }

            return fromLibrary ?? throw new InvalidDataException("unreadable solution");
        }

        if (fromLibrary is not null)
        {
            return fromLibrary;
        }

        var fromLines = ReadSlnProjectLines(full);
        if (fromLines.Count == 0)
        {
            throw new InvalidDataException("unreadable solution");
        }

        warnings.Add($"{name}: solution reader could not parse the file; using the project list read directly from it");
        return fromLines;
    }

    /// <summary>
    /// S4: the library drops projects under Folder elements nested in Folder elements without saying so.
    /// Counts alone can match while members differ, so the sets are compared; any difference warns and
    /// the union is returned, never a silently short list.
    /// </summary>
    internal static List<string> ReconcileSlnx(IReadOnlyList<string> fromLibrary, IReadOnlyList<string> fromXml, string name, List<string> warnings)
    {
        static string Key(string p) => p.Replace('\\', '/');
        var libKeys = new HashSet<string>(fromLibrary.Select(Key), StringComparer.OrdinalIgnoreCase);
        var xmlKeys = new HashSet<string>(fromXml.Select(Key), StringComparer.OrdinalIgnoreCase);
        if (libKeys.SetEquals(xmlKeys))
        {
            return fromLibrary.ToList();
        }

        warnings.Add($"{name}: solution reader found {fromLibrary.Count} projects but the file lists {fromXml.Count}; using every listed project");
        return fromLibrary.Concat(fromXml).ToList();
    }

    private static readonly Guid SolutionFolderType = new("2150E333-8FDC-42A3-9474-1A3956D46DE8");

    private static readonly System.Text.RegularExpressions.Regex ProjectLine = new(
        "^\\s*Project\\(\"\\{(?<type>[0-9A-Fa-f-]+)\\}\"\\)\\s*=\\s*\"[^\"]*\"\\s*,\\s*\"(?<path>[^\"]*)\"\\s*,\\s*\"\\{[^\"]*\\}\"",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static List<string> ReadSlnProjectLines(string full)
    {
        var result = new List<string>();
        foreach (var line in File.ReadLines(full))
        {
            var m = ProjectLine.Match(line);
            if (m.Success && Guid.TryParse(m.Groups["type"].Value, out var type) && type != SolutionFolderType)
            {
                result.Add(m.Groups["path"].Value);
            }
        }

        return result;
    }

    private static List<string> ReadSlnxProjectAttributes(string full)
    {
        // The solution is untrusted input: no DTDs, no external entities.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(full, settings);
        return XDocument.Load(reader).Descendants("Project")
            .Select(p => (string?)p.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();
    }

    private static List<string> Resolve(List<string> listed, string solutionDir, List<string> warnings)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in listed)
        {
            // Library output is solution-relative with '/'; raw slnx attributes may still carry '\'.
            var relative = raw.Replace('\\', '/');

            // Non-project entries (URL web sites, .sqlproj, .msbuildproj, .shproj) are out of scope on purpose.
            if (relative.Contains("://")
                || !ProjectExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(solutionDir, relative));
            if (!seen.Add(full))
            {
                continue;
            }

            if (!File.Exists(full))
            {
                warnings.Add($"{relative}: project file not found; skipped");
                continue;
            }

            result.Add(full);
        }

        return result;
    }
}
