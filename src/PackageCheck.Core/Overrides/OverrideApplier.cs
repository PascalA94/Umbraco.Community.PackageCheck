using NuGet.Versioning;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Overrides;

/// <summary>
/// H16: curated overrides, applied after the computed verdict (design 7.2 step 6, 7.8). Always labelled, and the computed
/// verdict is kept in <see cref="OverrideApplied.ComputedVerdict"/>.
/// </summary>
public static class OverrideApplier
{
    public const string OverrideSource = "override";

    public static PackageVerdict Apply(PackageVerdict computed, OverrideSet set, TargetVersion target, DateOnly today, bool anotherVersionClears)
    {
        // An override never stands in for a failed lookup: those stay unknown with their reason, never ok (design 7.3).
        // It does settle conflicting heuristic evidence, which is what curated data is for (design 7.4).
        if (computed is { Verdict: Verdict.Unknown, Reason: not UnknownReason.ConflictingEvidence }) return computed;
        if (computed.Package.Ref.Version is not { } version) return computed;
        var entry = Match(set, computed.Package.Ref.Id, version, target);
        if (entry is null) return computed;

        var (verdict, confidence) = entry.Status switch
        {
            OverrideStatus.Works => (Verdict.Ok, Confidence.High),
            // A broken entry that names a replacement is a rename or retirement the feeds cannot show (ruling R30).
            OverrideStatus.Broken when entry.Replacement is not null => (Verdict.Replaced, Confidence.High),
            OverrideStatus.Broken => (anotherVersionClears ? Verdict.Upgrade : Verdict.NoRelease, Confidence.High),
            _ => (Verdict.Suspicious, Confidence.Medium),
        };
        var label = entry.Origin == OverrideOrigin.Local ? "local override" : "override";
        var line = new Evidence("H16", entry.Status switch
            {
                OverrideStatus.Works => EvidenceResult.Positive,
                _ => EvidenceResult.Negative,
            }, entry.Status == OverrideStatus.Partial ? EvidenceWeight.Medium : EvidenceWeight.High, OverrideSource, entry.Summary,
            entry.Replacement);

        return computed with
        {
            Verdict = verdict,
            Confidence = confidence,
            Reason = null,
            Recommended = verdict switch
            {
                Verdict.Replaced => entry.Replacement,
                Verdict.Upgrade => computed.Recommended,
                _ => null,
            },
            Evidence = [.. computed.Evidence, line],
            Override = new OverrideApplied(entry, computed.Verdict, today > entry.ReviewBy, label),
        };
    }

    /// <summary>
    /// True when a <c>broken</c> or <c>partial</c> entry covers this upgrade candidate. A <c>works</c> entry never makes a
    /// candidate clear: curated data blocks bad versions but does not stand in for range evidence (plan review P11).
    /// </summary>
    public static bool BlocksCandidate(OverrideSet set, string id, NuGetVersion candidate, TargetVersion target) =>
        set.Entries.Any(e => e.Status != OverrideStatus.Works && Covers(e, id, candidate, target));

    /// <summary>Local beats community; within one origin the later entry wins (sources merge in order).</summary>
    public static OverrideEntry? Match(OverrideSet set, string id, NuGetVersion version, TargetVersion target) =>
        set.Entries.Where(e => Covers(e, id, version, target))
            .Select((e, i) => (e, i))
            .OrderBy(x => x.e.Origin == OverrideOrigin.Local ? 1 : 0)
            .ThenBy(x => x.i)
            .Select(x => x.e)
            .LastOrDefault();

    private static bool Covers(OverrideEntry e, string id, NuGetVersion version, TargetVersion target) =>
        e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
        && e.PackageVersions.Satisfies(version)
        && e.Umbraco.Satisfies(target.RangeVersion);
}
