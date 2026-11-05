using PackageCheck.Core.Facts;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Heuristics;

/// <summary>
/// One check from design 7.4. Synchronous and pure over <see cref="PackageFacts"/>: the collector does all I/O, so the
/// truth table and heuristic tests need no fakes (a refinement of the design's async sketch, docs/spikes.md).
/// </summary>
public interface IHeuristic
{
    string Id { get; }
    bool AppliesTo(TargetVersion t);
    IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t);
}
