using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>H04: is there a path to <c>Umbraco.Cms.*</c> within <see cref="FactsCollector.MaxDepth"/>? No path means not-umbraco (row 10).</summary>
public sealed class H04UmbracoPath : IHeuristic
{
    public string Id => "H04";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.Umbraco.Count == 0)
        {
            yield return new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.NuGet,
                $"no path to Umbraco.Cms.* within depth {FactsCollector.MaxDepth}");
            yield break;
        }
        foreach (var u in f.Umbraco.Where(u => u.Depth > 1))
            yield return new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.NuGet,
                $"{u.UmbracoPackageId} reached via {u.ViaPackageId} at depth {u.Depth}");
    }
}
