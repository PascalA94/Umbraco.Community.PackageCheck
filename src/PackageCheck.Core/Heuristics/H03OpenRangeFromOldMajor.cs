using PackageCheck.Core.Targets;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H03: an open range that starts before the new backoffice while the target is on it. Low negative; it raises
/// <c>suspicious</c>, never <c>no-release</c>. A range with no floor counts as below 14 (ruling R28).
/// </summary>
public sealed class H03OpenRangeFromOldMajor : IHeuristic
{
    public string Id => "H03";

    public bool AppliesTo(TargetVersion t) => t.RangeVersion.Major >= UmbracoFacts.NewBackofficeMajor;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (!AppliesTo(t) || !Ranges.AllAllow(f, t)) yield break;
        var old = Ranges.Open(f).Where(u => (u.Range.MinVersion?.Major ?? 0) < UmbracoFacts.NewBackofficeMajor).ToList();
        if (old.Count == 0) yield break;
        var min = old.Select(u => u.Range.MinVersion).Where(m => m is not null).Min();
        yield return new Evidence(Id, EvidenceResult.Negative, EvidenceWeight.Low, EvidenceSources.NuGet,
            $"open range >= {min?.ToNormalizedString() ?? "(no floor)"}");
    }
}
