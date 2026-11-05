using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H17 (cuttable #3): a leftover <c>App_Plugins/&lt;name&gt;</c> folder in the project whose name matches the last segment
/// of a package's static web assets <c>BasePath</c>, so old files can shadow the package's own. Medium negative,
/// evidence only: the truth table never reads it. A <c>BasePath</c> of "/" (web root) names no folder and is skipped.
/// </summary>
public sealed class H17LeftoverAppPlugins : IHeuristic
{
    public string Id => "H17";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.ProjectAppPlugins is not { Count: > 0 } folders) yield break;
        var names = f.OwnContents.Concat(f.Dependencies)
            .Select(c => c.StaticWebAssetBasePath?.Trim('/', '\\').Split('/', '\\')[^1])
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var folder in folders)
        {
            var name = folder[(folder.LastIndexOf('/') + 1)..];
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
                yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.Medium, "project",
                    $"leftover {folder} in the project may shadow the package's files");
        }
    }
}
