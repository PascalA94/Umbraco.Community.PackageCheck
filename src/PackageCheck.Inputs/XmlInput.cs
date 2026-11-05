using System.Xml;
using System.Xml.Linq;

namespace PackageCheck.Inputs;

/// <summary>Shared helpers for reading untrusted MSBuild XML and walking up from a project folder.</summary>
internal static class XmlInput
{
    // Repos are untrusted: no DTDs, no external entities, and a cap so a huge file cannot exhaust memory.
    private const long MaxCharacters = 10_000_000;

    public static XDocument Load(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters,
        };
        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(reader);
    }

    /// <summary>Old-style csproj files carry an xmlns, so names are matched by local name.</summary>
    public static IEnumerable<XElement> Elements(XDocument doc, string localName) =>
        doc.Descendants().Where(e => e.Name.LocalName == localName);

    public static string? Attr(XElement e, string name) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    /// <summary>True when the element or any ancestor (ItemGroup, Choose, When) carries a Condition. Conditions are never evaluated.</summary>
    public static bool IsConditioned(XElement e) =>
        e.AncestorsAndSelf().Any(a => a.Attributes().Any(x => x.Name.LocalName == "Condition"));

    public static string? AttrOrChild(XElement e, string name) =>
        Attr(e, name) ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value.Trim();

    /// <summary>
    /// Folders from <paramref name="startDir"/> upward, nearest first. Stops after <paramref name="stopAt"/> when
    /// given, otherwise after the first folder holding a .git entry (the repo root), otherwise at the filesystem root.
    /// </summary>
    public static IEnumerable<string> Ancestors(string startDir, string? stopAt)
    {
        var stop = stopAt is null ? null : Normalise(stopAt);
        var dir = Normalise(startDir);
        while (true)
        {
            yield return dir;
            if (stop is not null && string.Equals(dir, stop, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            var gitPath = Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                yield break;
            }

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent))
            {
                yield break;
            }

            dir = parent;
        }
    }

    private static string Normalise(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is { Length: > 0 } s ? s : Path.GetFullPath(p);
}
