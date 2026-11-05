using System.Globalization;
using PackageCheck.Core.Targets;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// H18: the package's newest release predates the target major's first release. Info only. The major's release date
/// comes from <c>umbraco-versions.json</c> (<see cref="UmbracoMajor.Released"/>); when either date is unknown, nothing.
/// </summary>
public sealed class H18StaleRelease(UmbracoVersionsTable table) : IHeuristic
{
    public string Id => "H18";

    public bool AppliesTo(TargetVersion t) => true;

    public IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t)
    {
        if (f.LatestPublished is not { } last || table.Find(t.Resolved.Major)?.Released is not { } released) yield break;
        var lastDay = DateOnly.FromDateTime(last.UtcDateTime);
        if (lastDay >= released) yield break;
        yield return new Evidence(Id, EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.NuGet,
            string.Create(CultureInfo.InvariantCulture,
                $"last release {lastDay:yyyy-MM-dd} is older than Umbraco {t.Resolved.Major} (released {released:yyyy-MM-dd})"));
    }
}
