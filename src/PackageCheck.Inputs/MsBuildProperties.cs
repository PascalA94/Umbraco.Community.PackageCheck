using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PackageCheck.Inputs;

/// <summary>
/// A flat view of MSBuild properties: Directory.Build.props files from the repo root down to the project folder,
/// then the project's own PropertyGroups. Conditions are ignored and nothing is evaluated beyond $(Name)
/// substitution, so a property whose value depends on a condition resolves to its last unconditional-looking
/// definition. The tool never runs MSBuild (design: read-only, no code execution from scanned repos).
/// </summary>
public sealed class MsBuildProperties
{
    private const int MaxExpansionDepth = 16;
    private const int MaxSubstitutions = 10_000;
    private const int MaxExpandedLength = 100_000;
    private static readonly Regex Reference = new(@"\$\(([^)]*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly Dictionary<string, string> raw;

    private MsBuildProperties(Dictionary<string, string> raw, List<string> warnings)
    {
        this.raw = raw;
        Warnings = warnings;
    }

    /// <summary>Warnings raised while reading props files (for example a malformed Directory.Build.props).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <param name="stopAt">Topmost folder to read from; defaults to the repo root (the first folder with a .git entry).</param>
    public static MsBuildProperties ForProject(string csprojPath, string? stopAt = null)
    {
        var fullProject = Path.GetFullPath(csprojPath);
        var dirs = XmlInput.Ancestors(Path.GetDirectoryName(fullProject)!, stopAt).Reverse();
        var props = new MsBuildProperties(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);
        var warnings = (List<string>)props.Warnings;

        foreach (var dir in dirs)
        {
            var file = Path.Combine(dir, "Directory.Build.props");
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                props.AddFrom(XmlInput.Load(file));
            }
            catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
            {
                // The exception text is not echoed: it can quote file content.
                warnings.Add($"{Path.GetFileName(dir)}/Directory.Build.props: could not be read; its properties were ignored");
            }
        }

        try
        {
            if (File.Exists(fullProject))
            {
                props.AddFrom(XmlInput.Load(fullProject));
            }
        }
        catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            // ProjectFileReader reports an unreadable project itself.
        }

        return props;
    }

    /// <summary>The expanded value of a property, or null when it is not set or cannot be resolved.</summary>
    public string? Get(string name) => raw.TryGetValue(name, out var v) ? Expand(v) : null;

    /// <summary>Returns <paramref name="raw"/> with every $(Name) substituted, or null when any reference stays unresolved.</summary>
    public string? Expand(string raw)
    {
        // The budget bounds total work, not just depth: a hostile repo can fan out ($(A)$(A)... per level)
        // so that a depth cap alone still allows exponential substitution. Over budget means unresolved.
        var budget = MaxSubstitutions;
        return ExpandCore(raw, 0, ref budget);
    }

    /// <summary>A copy with extra properties layered on top, used for the properties a Directory.Packages.props defines.</summary>
    internal MsBuildProperties With(XDocument doc)
    {
        var copy = new MsBuildProperties(new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase), [.. Warnings]);
        copy.AddFrom(doc);
        return copy;
    }

    private void AddFrom(XDocument doc)
    {
        foreach (var group in XmlInput.Elements(doc, "PropertyGroup"))
        {
            foreach (var p in group.Elements())
            {
                raw[p.Name.LocalName] = p.Value.Trim();
            }
        }
    }

    private string? ExpandCore(string text, int depth, ref int budget)
    {
        if (depth > MaxExpansionDepth || budget <= 0)
        {
            // A property that refers back to itself never resolves.
            return null;
        }

        var failed = false;
        var remaining = budget;
        var result = Reference.Replace(text, m =>
        {
            if (failed)
            {
                return m.Value;
            }

            if (--remaining <= 0 || !raw.TryGetValue(m.Groups[1].Value, out var value))
            {
                failed = true;
                return m.Value;
            }

            var inner = ExpandCore(value, depth + 1, ref remaining);
            if (inner is null || inner.Length > MaxExpandedLength)
            {
                failed = true;
                return m.Value;
            }

            return inner;
        });
        budget = remaining;
        return failed || result.Length > MaxExpandedLength ? null : result;
    }
}
