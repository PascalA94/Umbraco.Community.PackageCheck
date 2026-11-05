using PackageCheck.Core.Targets;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H15: among versions newer than the current one, only prereleases clear the target. Info only. Reads
/// <see cref="PackageFacts.ClearingVersions"/>, which the engine fills while judging upgrade candidates (Task 17).
/// </summary>
public sealed class H15PrereleaseOnly : IHeuristic
{
    public string Id => "H15";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.ClearingVersions is not { } clearing) yield break;
        var newer = clearing.Where(v => v > f.Version).ToList();
        if (newer.Count == 0 || newer.Any(v => !v.IsPrerelease)) yield break;
        yield return new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.NuGet,
            $"only a prerelease supports the target: {newer.Min()!.ToNormalizedString()}");
    }
}
