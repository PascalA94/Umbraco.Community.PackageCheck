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
public sealed class ProjectReader : IProjectReader
{
    private const string LockFileName = "packages.lock.json";

    public Task<ScanInput> ReadAsync(string path, InputOptions o, CancellationToken ct)
    {
        // Readers are synchronous file IO over a handful of small files; Task keeps the seam open for MSBuild later.
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

        var projects = new List<ProjectInfo>();
        var anyLock = false;
        foreach (var csproj in projectPaths)
        {
            ct.ThrowIfCancellationRequested();
            var (info, projectWarnings) = ProjectFileReader.ReadSafe(csproj, o.StopAt);
            warnings.AddRange(projectWarnings);
            if (info is null)
            {
                continue;
            }

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

        var input = new ScanInput(sitePath, projects, warnings, status);
        if (input.SiteUmbracoVersion is null)
        {
            throw new NoUmbracoProjectException(path);
        }

        var majors = projects.Select(p => p.UmbracoVersion).Where(v => v is not null)
            .Select(v => v!.Major).Distinct().Order().ToList();
        if (majors.Count > 1)
        {
            warnings.Add($"mixed Umbraco majors: {string.Join(", ", majors)}; using the highest");
        }

        return Task.FromResult(input);
    }

    private static ProjectInfo AppendTransitive(ProjectInfo project, IReadOnlyList<PackageRef> lockRefs)
    {
        var known = new HashSet<string>(project.Packages.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        var extra = lockRefs.Where(r => !r.Direct && known.Add(r.Id));
        return project with { Packages = [.. project.Packages, .. extra] };
    }
}
