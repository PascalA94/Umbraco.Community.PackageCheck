using System.Text.RegularExpressions;
using System.Xml.Linq;
using NuGet.Versioning;
using PackageCheck.Core.Model;

namespace PackageCheck.Inputs;

/// <summary>
/// Reads PackageReference items from a csproj with XDocument and NuGet.Versioning only (no MSBuild evaluation).
/// Version precedence: VersionOverride, then the central package version, then the project's own Version.
/// Versions that need evaluation or a restore (open ranges, floating, unresolved properties) are left unresolved
/// with the raw text kept, never guessed (design: unknown with reason, never ok).
/// </summary>
public static class ProjectFileReader
{
    private const string UnresolvedPropertyPrefix = "unresolved MSBuild property";

    /// <summary>Reads one project; throws <see cref="InvalidDataException"/> when it cannot be read. Prefer <see cref="ReadSafe"/>.</summary>
    public static ProjectInfo Read(string csprojPath, string? stopAt = null)
    {
        var (info, warnings) = ReadSafe(csprojPath, stopAt);
        return info ?? throw new InvalidDataException(warnings.FirstOrDefault() ?? "project could not be read");
    }

    /// <summary>
    /// Never throws for bad input: a missing or malformed project is a warning and a null result (plan review P13).
    /// Warnings also report a malformed Directory.Build.props or Directory.Packages.props and duplicate references.
    /// </summary>
    public static (ProjectInfo? Info, IReadOnlyList<string> Warnings) ReadSafe(string csprojPath, string? stopAt = null)
    {
        try
        {
            return ReadCore(csprojPath, stopAt);
        }
        catch (RegexMatchTimeoutException)
        {
            // Property expansion is regex based; a hostile file that times it out is skipped, not fatal.
            return (null, [$"{Path.GetFileName(csprojPath)}: took too long to read; skipped"]);
        }
    }

    private static (ProjectInfo? Info, IReadOnlyList<string> Warnings) ReadCore(string csprojPath, string? stopAt)
    {
        var warnings = new List<string>();
        var full = Path.GetFullPath(csprojPath);
        var name = Path.GetFileName(full);
        if (!File.Exists(full))
        {
            return (null, [$"{name}: project file not found; skipped"]);
        }

        XDocument doc;
        try
        {
            doc = XmlInput.Load(full);
        }
        catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            // Exception text is not echoed: it can quote file content.
            return (null, [$"{name}: could not be read as a project file; skipped"]);
        }

        var props = MsBuildProperties.ForProject(full, stopAt);
        warnings.AddRange(props.Warnings);
        var cpm = CentralPackageVersions.ForProject(full, props, stopAt);
        if (cpm?.Warning is { } cpmWarning)
        {
            warnings.Add(cpmWarning);
        }

        var packages = new List<PackageRef>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var groups = new List<(string Id, List<(string? Text, bool Conditioned)> Entries)>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in XmlInput.Elements(doc, "PackageReference"))
        {
            var id = XmlInput.Attr(item, "Include")?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var text = XmlInput.AttrOrChild(item, "VersionOverride");
            if (string.IsNullOrWhiteSpace(text) && cpm is not null)
            {
                text = cpm.VersionFor(id) ?? cpm.ConflictFor(id);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = XmlInput.AttrOrChild(item, "Version");
            }

            if (!index.TryGetValue(id, out var i))
            {
                index[id] = groups.Count;
                groups.Add((id, []));
                i = groups.Count - 1;
            }

            groups[i].Entries.Add((text, XmlInput.IsConditioned(item)));
        }

        foreach (var (id, entries) in groups)
        {
            seen[id] = id;
            if (entries.Count > 1)
            {
                warnings.Add($"{name}: package {id} is referenced more than once; the first entry is used");
            }

            var distinct = entries.Select(e => e.Text).Distinct(StringComparer.Ordinal).ToList();
            if (entries.Count > 1 && entries.Any(e => e.Conditioned) && distinct.Count > 1)
            {
                // Conditions are not evaluated, so which entry applies is unknown; guessing would hide a version.
                var raw = string.Join("; ", distinct.Where(t => !string.IsNullOrWhiteSpace(t)));
                packages.Add(new PackageRef(id, raw, null, true, $"conditional versions differ: {raw}"));
                continue;
            }

            var text = entries[0].Text;
            if (cpm?.ConflictFor(id) is { } central && entries.Count == 1 && string.Equals(text, central, StringComparison.Ordinal))
            {
                packages.Add(new PackageRef(id, central, null, true, $"conditional versions differ: {central}"));
                continue;
            }

            packages.Add(cpm?.Warning is not null && string.IsNullOrWhiteSpace(text)
                ? new PackageRef(id, null, null, true, $"{UnresolvedPropertyPrefix}: central package versions file could not be read")
                : Resolve(id, text, props));
        }

        if (cpm is not null)
        {
            // GlobalPackageReference applies to every project under the props file but never overrides a project's own entry.
            foreach (var g in cpm.GlobalReferences.Where(g => seen.TryAdd(g.Id, g.Id)))
            {
                packages.Add(Resolve(g.Id, g.VersionText, props));
            }
        }

        var umbraco = PackageRefs.UmbracoVersionOf(packages);

        return (new ProjectInfo(full, ReadTfms(props), umbraco, packages), warnings);
    }

    private static IReadOnlyList<string> ReadTfms(MsBuildProperties props)
    {
        var text = props.Get("TargetFrameworks") ?? props.Get("TargetFramework");
        return text is null
            ? []
            : text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static PackageRef Resolve(string id, string? text, MsBuildProperties props)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PackageRef(id, null, null, true, "no version specified");
        }

        text = text.Trim();
        var expanded = props.Expand(text);
        if (expanded is null)
        {
            return new PackageRef(id, text, null, true, $"{UnresolvedPropertyPrefix}: {text}");
        }

        expanded = expanded.Trim();
        if (NuGetVersion.TryParse(expanded, out var bare))
        {
            // NuGet reads a bare "6.2.1" as the minimum-inclusive range [6.2.1, ), and restore picks exactly it.
            return new PackageRef(id, text, bare, true, null);
        }

        if (!VersionRange.TryParse(expanded, out var range))
        {
            return new PackageRef(id, text, null, true, $"unparseable version: {text}");
        }

        if (range.IsFloating)
        {
            // Needs a lock file or restore to know what was picked (design Review Focus 2).
            return new PackageRef(id, text, null, true, $"{UnresolvedPropertyPrefix}: {text} (floating version)");
        }

        if (range is { HasLowerBound: true, HasUpperBound: true, IsMinInclusive: true, IsMaxInclusive: true }
            && range.MinVersion == range.MaxVersion)
        {
            return new PackageRef(id, text, range.MinVersion, true, null);
        }

        return new PackageRef(id, text, null, true, $"{UnresolvedPropertyPrefix}: {text} (open version range)");
    }
}
