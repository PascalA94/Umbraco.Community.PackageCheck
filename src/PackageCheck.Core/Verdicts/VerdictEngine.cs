using System.Collections.Concurrent;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Verdicts;

public interface IVerdictEngine
{
    Task<PackageVerdict> EvaluateAsync(SitePackage pkg, TargetVersion target, ScanOptions o, CancellationToken ct);
}

/// <summary>
/// Judges one site package (design 7.2): facts, heuristics, truth table, the upgrade walk for row 1, then replacement by
/// H13/H14 (ruling R27). Source failures become <c>unknown</c> with their reason, never <c>ok</c> (design 7.3).
/// Overrides are applied last (H16); a broken or partial entry also blocks that version as an upgrade candidate.
/// </summary>
public sealed class VerdictEngine(FactsCollector facts, OverrideSet overrides, TimeProvider clock, IReadOnlyList<IHeuristic>? heuristics = null)
    : IVerdictEngine
{
    private readonly IReadOnlyList<IHeuristic> _heuristics = heuristics ?? HeuristicSet.Metadata(null);
    private readonly H15PrereleaseOnly _h15 = new();

    public async Task<PackageVerdict> EvaluateAsync(SitePackage pkg, TargetVersion target, ScanOptions o, CancellationToken ct)
    {
        if (pkg.Ref.Version is not { } version)
            return Unknown(pkg, UnknownReason.UnresolvedProperty, "project",
                $"version '{pkg.Ref.VersionText}' could not be resolved to a single version");

        PackageFacts f;
        try
        {
            f = await facts.CollectAsync(pkg.Ref.Id, version, target, new FactsOptions(o.Inspect, pkg.Projects), ct);
        }
        catch (PackageSourceException e)
        {
            // The message is built from redacted feed URLs only (PackageSourceException), so it is safe to show.
            return Unknown(pkg, e.Reason, EvidenceSources.NuGet, e.Message);
        }

        // An unresolvable dependency means "no path to Umbraco.Cms" is unproven; saying not-umbraco would hide it.
        if (f.Umbraco.Count == 0 && !f.NoCompatibleGroup && f.MissingDependencies is { Count: > 0 } missing)
            return Unknown(pkg, UnknownReason.NotFound, EvidenceSources.NuGet,
                $"dependency {string.Join(", ", missing)} was not found on any feed, so whether this is an Umbraco package is unknown");

        try
        {
            return await JudgeAsync(pkg, f, target, o, ct);
        }
        catch (PackageSourceException e)
        {
            // A candidate in the upgrade walk failed: the verdict cannot be trusted either way (design 7.3).
            return Unknown(pkg, e.Reason, EvidenceSources.NuGet, e.Message);
        }
    }

    private async Task<PackageVerdict> JudgeAsync(SitePackage pkg, PackageFacts f, TargetVersion target, ScanOptions o, CancellationToken ct)
    {
        var evidence = Evaluate(f, target);
        var table = TruthTable.Decide(evidence, f);
        var verdict = new PackageVerdict(pkg, table.Verdict, table.Confidence, table.Reason, null, null, null, evidence, f.Marketplace, null);
        var recommendedDeprecated = false;
        UpgradeChoice? choice = null;
        var candidates = new ConcurrentDictionary<NuGetVersion, PackageFacts?>();
        var clears = new ConcurrentDictionary<NuGetVersion, bool>();

        async Task<UpgradeChoice> WalkAsync()
        {
            async Task<bool> MetadataClears(NuGetVersion v)
            {
                if (clears.TryGetValue(v, out var known)) return known;
                if (OverrideApplier.BlocksCandidate(overrides, f.Id, v, target)) return clears[v] = false;
                var cf = await CandidateAsync(f.Id, v, target, candidates, ct);
                return clears[v] = cf is { Details.Listed: true } && TruthTable.Decide(Evaluate(cf, target), cf).Clears;
            }

            // Inspection of candidates arrives with Task 21; until then a metadata clear is a full clear.
            return await UpgradeSelector.SelectAsync(f.Version, f.AllVersions, MetadataClears, _ => Task.FromResult(true), o.AllowPrerelease);
        }

        if (table.Row == 1)
        {
            choice = await WalkAsync();
            var withClearing = f with { ClearingVersions = clears.Where(kv => kv.Value).Select(kv => kv.Key).Order().ToList() };
            evidence = [.. evidence, .. _h15.Evaluate(withClearing, target)];
            recommendedDeprecated = choice.Recommended is { } r && candidates.GetValueOrDefault(r)?.Details.Deprecation is not null;
            verdict = WithChoice(verdict with
            {
                Verdict = choice.Recommended is null ? Verdict.NoRelease : Verdict.Upgrade,
                Confidence = Confidence.High,
                Evidence = evidence,
            }, choice);
        }

        verdict = ApplyReplacement(verdict, table.Row, recommendedDeprecated);

        // "broken -> upgrade if another version clears" needs the walk even when the computed row is not 1 (design 7.8).
        var entry = OverrideApplier.Match(overrides, f.Id, f.Version, target);
        if (entry is { Status: OverrideStatus.Broken, Replacement: null } && choice is null) choice = await WalkAsync();
        var applied = OverrideApplier.Apply(verdict, overrides, target, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            anotherVersionClears: choice?.Recommended is not null);
        return applied is { Override: not null, Verdict: Verdict.Upgrade } && choice is not null ? WithChoice(applied, choice) : applied;
    }

    private static PackageVerdict WithChoice(PackageVerdict v, UpgradeChoice c) => v with
    {
        Recommended = c.Recommended?.ToNormalizedString(),
        HighestCompatible = c.HighestCompatible?.ToNormalizedString(),
        PrereleaseCompatible = c.PrereleaseCompatible?.ToNormalizedString(),
        LatestStable = c.LatestStable?.ToNormalizedString(),
        LatestStableClears = c.LatestStableClears,
    };


    private IReadOnlyList<Evidence> Evaluate(PackageFacts f, TargetVersion t) =>
        _heuristics.Where(h => h.AppliesTo(t)).SelectMany(h => h.Evaluate(f, t)).ToList();

    private async Task<PackageFacts?> CandidateAsync(string id, NuGetVersion v, TargetVersion t,
        ConcurrentDictionary<NuGetVersion, PackageFacts?> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(v, out var hit)) return hit;
        try
        {
            return cache[v] = await facts.CollectAsync(id, v, t, new FactsOptions(Inspect: false, [], Marketplace: false), ct);
        }
        catch (PackageSourceException e) when (e.Reason == UnknownReason.NotFound)
        {
            // Listed in the version index but without details: not a candidate. Other failures make the package unknown.
            return cache[v] = null;
        }
    }

    /// <summary>
    /// Ruling R27: high H13/H14 turn the verdict into <c>replaced</c> (high), naming the alternate, except on rows 5-8 and
    /// on a row-1 upgrade to a non-deprecated version of an id that is not retired. Never over unsafe-archive (R32).
    /// </summary>
    private static PackageVerdict ApplyReplacement(PackageVerdict v, int row, bool recommendedDeprecated)
    {
        var replacement = v.Evidence.FirstOrDefault(e => e.Check is "H13" or "H14" && e.IsHighNegative && e.Alternate is not null);
        // Row 10: a package outside Umbraco is never "replaced"; it is hidden unless --verbose, and must not feed --fail-on.
        if (replacement is null || row is >= 5 and <= 8 or 10 || v.Reason == UnknownReason.UnsafeArchive) return v;
        // Marketplace retirement is per id, so an upgrade within a retired id does not escape it.
        var retiredId = v.Evidence.Any(e => e.Check == "H13" && e.IsHighNegative);
        if (row == 1 && v.Verdict == Verdict.Upgrade && !recommendedDeprecated && !retiredId) return v;
        return v with { Verdict = Verdict.Replaced, Confidence = Confidence.High, Reason = null, Recommended = replacement.Alternate };
    }

    private static PackageVerdict Unknown(SitePackage pkg, UnknownReason reason, string source, string detail) =>
        new(pkg, Verdict.Unknown, null, reason, null, null, null,
            [new Evidence("source", EvidenceResult.Info, EvidenceWeight.Info, source, detail)], null, null);
}
