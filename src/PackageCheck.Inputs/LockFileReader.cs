using System.Text.Json;
using NuGet.Versioning;
using PackageCheck.Core.Model;

namespace PackageCheck.Inputs;

/// <summary>
/// Reads packages.lock.json (version 2) for one target framework. The file is untrusted input: depth and size are
/// capped, and anything unreadable becomes a warning, never an exception (design Review Focus 2: a lock file is the
/// only source that can resolve floating versions and the only offline source of transitive packages).
/// </summary>
public static class LockFileReader
{
    private const long MaxBytes = 32 * 1024 * 1024;
    private const int MaxDepth = 32;

    public static (IReadOnlyList<PackageRef> Refs, IReadOnlyList<string> Warnings) Read(string lockPath, string tfm)
    {
        var name = Path.GetFileName(lockPath);
        try
        {
            if (!File.Exists(lockPath))
            {
                return ([], [$"{name}: lock file not found; ignored"]);
            }

            if (new FileInfo(lockPath).Length > MaxBytes)
            {
                return ([], [$"{name}: lock file is too large; ignored"]);
            }

            using var stream = File.OpenRead(lockPath);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxDepth });
            return ReadDocument(doc.RootElement, name, tfm);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Exception text is not echoed: it can quote file content.
            return ([], [$"{name}: could not be read as a lock file; ignored"]);
        }
    }

    /// <summary>
    /// Overlays locked versions onto the project's direct references. The locked version wins, including over a
    /// version the csproj resolved itself (restore is what actually happened) and clears any unresolved reason.
    /// Transitive lock entries never touch a direct reference. UmbracoVersion is recomputed afterwards.
    /// A direct reference the lock lacks (a stale lock) is left as it was and reported as a warning, so a floating
    /// version cannot stay unknown silently.
    /// </summary>
    public static (ProjectInfo Project, IReadOnlyList<string> Warnings) Apply(ProjectInfo project, IReadOnlyList<PackageRef> lockRefs)
    {
        var locked = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in lockRefs.Where(r => r is { Direct: true, Version: not null }))
        {
            locked[r.Id] = r.Version!;
        }

        var packages = project.Packages
            .Select(p => p.Direct && locked.TryGetValue(p.Id, out var v) ? p with { Version = v, UnresolvedReason = null } : p)
            .ToList();

        var warnings = project.Packages
            .Where(p => p.Direct && !locked.ContainsKey(p.Id))
            .Select(p => $"{Path.GetFileName(project.Path)}: lock file has no entry for {p.Id}")
            .ToList();

        return (project with { Packages = packages, UmbracoVersion = PackageRefs.UmbracoVersionOf(packages) }, warnings);
    }

    private static (IReadOnlyList<PackageRef>, IReadOnlyList<string>) ReadDocument(JsonElement root, string name, string tfm)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var v))
        {
            return ([], [$"{name}: lock file has no version; ignored"]);
        }

        if (v != 2)
        {
            return ([], [$"{name}: lock file version {v} is not supported (version 2 is); ignored"]);
        }

        if (!root.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Object)
        {
            return ([], [$"{name}: lock file has no dependencies section; ignored"]);
        }

        // Keys are "net8.0" or "net8.0/win-x64"; the plain TFM entry is the one a framework-dependent project restores.
        var section = deps.EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, tfm, StringComparison.OrdinalIgnoreCase));
        if (section.Value.ValueKind != JsonValueKind.Object)
        {
            return ([], [$"{name}: lock file has no section for target framework {tfm}; ignored"]);
        }

        var refs = new List<PackageRef>();
        foreach (var entry in section.Value.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = Str(entry.Value, "type");
            if (type is null || string.Equals(type, "Project", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!NuGetVersion.TryParse(Str(entry.Value, "resolved"), out var resolved))
            {
                continue;
            }

            var direct = string.Equals(type, "Direct", StringComparison.OrdinalIgnoreCase);
            refs.Add(new PackageRef(entry.Name, resolved.ToNormalizedString(), resolved, direct, null));
        }

        return (refs, []);
    }

    private static string? Str(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
