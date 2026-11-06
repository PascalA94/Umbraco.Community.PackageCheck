using PackageCheck.Core;
using PackageCheck.Core.Model;

namespace PackageCheck.Inputs;

public interface IProjectReader
{
    Task<ScanInput> ReadAsync(string path, InputOptions o, CancellationToken ct);
}

/// <summary>
/// Turns a path into a <see cref="ScanInput"/>: finds the entry point, reads every project, overlays lock files and
/// applies the site rules (design 6.1). Warnings from every reader are kept as raw text; escaping is an output concern.
/// </summary>
public sealed class ProjectReader(MsBuildListRunner? msbuild = null) : IProjectReader
{
    private const string LockFileName = "packages.lock.json";

    public async Task<ScanInput> ReadAsync(string path, InputOptions o, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = FolderDiscovery.FindEntry(path);
        var warnings = new List<string>();

        IReadOnlyList<string> projectPaths;
        string sitePath;
        switch (entry.Kind)
        {
            case EntryKind.Slnx or EntryKind.Sln:
                var solution = SolutionFileReader.Read(entry.Path);
                warnings.AddRange(solution.Warnings);
                projectPaths = solution.ProjectPaths;
                sitePath = entry.Path;
                break;
            case EntryKind.Csproj:
                projectPaths = [entry.Path];
                sitePath = entry.Path;
                break;
            default:
                projectPaths = FolderDiscovery.FindProjects(entry.Path);
                sitePath = entry.Path;
                break;
        }

        var fromFiles = new List<ProjectInfo>();
        foreach (var csproj in projectPaths)
        {
            ct.ThrowIfCancellationRequested();
            var (info, projectWarnings) = ProjectFileReader.ReadSafe(csproj, o.StopAt);
            warnings.AddRange(projectWarnings);
            if (info is not null)
            {
                fromFiles.Add(info);
            }
        }

        var fromMsBuild = o.UseMsBuild && msbuild is not null && fromFiles.Count > 0
            ? await TryMsBuildAsync(fromFiles, o.Transitive, warnings, ct)
            : null;
        var (projects, status) = fromMsBuild is not null
            ? (fromMsBuild, o.Transitive ? TransitiveStatus.FromMsBuild : TransitiveStatus.NotRequested)
            : ApplyLockFiles(fromFiles, o, warnings);

        var input = new ScanInput(sitePath, projects, warnings, status);
        if (input.SiteUmbracoVersion is null)
        {
            // A folder that resolved to a solution or project file names it (folder rule: .slnx, then .sln, then *.csproj).
            var resolvedFile = entry.Kind is EntryKind.Slnx or EntryKind.Sln or EntryKind.Csproj
                               && !string.Equals(Path.GetFullPath(entry.Path), Path.GetFullPath(path), StringComparison.Ordinal);
            throw new NoUmbracoProjectException(path, resolvedFile ? entry.Path : null);
        }

        var majors = projects.Select(p => p.UmbracoVersion).Where(v => v is not null)
            .Select(v => v!.Major).Distinct().Order().ToList();
        if (majors.Count > 1)
        {
            warnings.Add($"mixed Umbraco majors: {string.Join(", ", majors)}; using the highest");
        }

        return input;
    }

    /// <summary>
    /// <c>--msbuild</c>: every project's exact versions from <c>dotnet list package</c>. All or nothing: if any project
    /// fails, the whole site falls back to file reading with one warning, so a site never mixes the two sources.
    /// </summary>
    private async Task<List<ProjectInfo>?> TryMsBuildAsync(List<ProjectInfo> fromFiles, bool transitive, List<string> warnings, CancellationToken ct)
    {
        var result = new List<ProjectInfo>();
        foreach (var info in fromFiles)
        {
            IReadOnlyList<PackageRef> refs;
            try
            {
                refs = await msbuild!.ListAsync(info.Path, transitive, ct);
            }
            catch (MsBuildListException e)
            {
                warnings.Add($"--msbuild: {e.Message} for {Path.GetFileName(info.Path)}; run dotnet restore first. Reading project files instead");
                return null;
            }

            var exact = refs.Where(r => r.Direct).GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var packages = info.Packages
                .Select(p => exact.TryGetValue(p.Id, out var r) ? p with { Version = r.Version, UnresolvedReason = null } : p)
                .ToList();
            var resolved = info with { Packages = packages, UmbracoVersion = PackageRefs.UmbracoVersionOf(packages) };
            result.Add(transitive ? AppendTransitive(resolved, refs) : resolved);
        }
        return result;
    }

    private static (List<ProjectInfo>, TransitiveStatus) ApplyLockFiles(List<ProjectInfo> fromFiles, InputOptions o, List<string> warnings)
    {
        var projects = new List<ProjectInfo>();
        var anyLock = false;
        foreach (var file in fromFiles)
        {
            var info = file;

            var lockPath = Path.Combine(Path.GetDirectoryName(info.Path)!, LockFileName);
            // The first target framework stands in for the project: a lock file is per TFM and the rules judge one.
            if (File.Exists(lockPath) && info.Tfms.Count > 0)
            {
                anyLock = true;
                var (refs, lockWarnings) = LockFileReader.Read(lockPath, info.Tfms[0]);
                warnings.AddRange(lockWarnings);
                var (applied, applyWarnings) = LockFileReader.Apply(info, refs);
                warnings.AddRange(applyWarnings);
                info = o.Transitive ? AppendTransitive(applied, refs) : applied;
            }

            projects.Add(info);
        }

        var status = !o.Transitive ? TransitiveStatus.NotRequested
            : anyLock ? TransitiveStatus.FromLockFile
            : TransitiveStatus.Unavailable;
        if (status == TransitiveStatus.Unavailable && projects.Count > 0)
        {
            warnings.Add($"transitive packages unavailable: no {LockFileName} found next to a project");
        }
        return (projects, status);
    }

    private static ProjectInfo AppendTransitive(ProjectInfo project, IReadOnlyList<PackageRef> lockRefs)
    {
        var known = new HashSet<string>(project.Packages.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        var extra = lockRefs.Where(r => !r.Direct && known.Add(r.Id));
        return project with { Packages = [.. project.Packages, .. extra] };
    }
}
