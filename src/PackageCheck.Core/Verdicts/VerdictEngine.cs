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
    private readonly IReadOnlyList<IHeuristic> _heuristics = heuristics ?? HeuristicSet.All(null);
    private readonly H15PrereleaseOnly _h15 = new();
    private readonly H06TfmFit _h06 = new();

    public async Task<PackageVerdict> EvaluateAsync(SitePackage pkg, TargetVersion target, ScanOptions o, CancellationToken ct)
    {
        try
        {
            return await EvaluateCoreAsync(pkg, target, o, ct);
        }
        catch (Exception e) when (e is not (OperationCanceledException or OutOfMemoryException or PackageSourceException))
        {
            // One hostile package or library bug must not abort the whole site (security review M3, S2). The message is
            // fixed: an exception's own text can quote package-controlled bytes.
            return Unknown(pkg, UnknownReason.SourceError, "tool", "this package could not be judged (unexpected error while reading it)");
        }
    }

    private async Task<PackageVerdict> EvaluateCoreAsync(SitePackage pkg, TargetVersion target, ScanOptions o, CancellationToken ct)
    {
        if (pkg.Ref.Version is not { } version)
            return Unknown(pkg, UnknownReason.UnresolvedProperty, "project",
                $"version '{pkg.Ref.VersionText}' could not be resolved to a single version");

        PackageFacts f;
        try
        {
            f = await facts.CollectAsync(pkg.Ref.Id, version, target, new FactsOptions(o.Inspect, pkg.ProjectDirectories ?? []), ct);
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
        // Candidates whose contents could not be read, with the reason (offline, over the cap, not on the feed).
        var uninspected = new ConcurrentDictionary<NuGetVersion, string>();

        async Task<UpgradeChoice> WalkAsync()
        {
            async Task<bool> MetadataClears(NuGetVersion v)
            {
                if (clears.TryGetValue(v, out var known)) return known;
                if (OverrideApplier.BlocksCandidate(overrides, f.Id, v, target)) return clears[v] = false;
                var cf = await CandidateAsync(f.Id, v, target, candidates, ct);
                return clears[v] = cf is { Details.Listed: true } && TruthTable.Decide(Evaluate(cf, target), cf).Clears;
            }

            // Design 6.1: only the chosen candidate is inspected (bounded by UpgradeSelector.MaxInspections).
            async Task<bool> FullyClears(NuGetVersion v)
            {
                if (!o.Inspect) return true;
                try
                {
                    var cf = await facts.CollectAsync(f.Id, v, target, new FactsOptions(Inspect: true, [], Marketplace: false, AssetDependencies: false), ct);
                    if (!cf.ContentsKnown) uninspected[v] = cf.InspectionNotes?.FirstOrDefault() ?? "nupkg not available";
                    return TruthTable.Decide(Evaluate(cf, target), cf).Clears;
                }
                catch (PackageSourceException e) when (e.Reason == UnknownReason.UnsafeArchive)
                {
                    // A hostile candidate is never recommended; the current version's verdict is not about its archive.
                    return false;
                }
            }

            // H06 on the candidate's metadata (or contents, if already read): no H06 evidence means nothing is known,
            // which counts as a fit so a package without TFM data is never pushed to a later major.
            async Task<bool> FitsTargetTfm(NuGetVersion v)
            {
                var cf = await CandidateAsync(f.Id, v, target, candidates, ct);
                return cf is null || !_h06.Evaluate(cf, target).Any(e => e.Result == EvidenceResult.Negative);
            }

            // Every Umbraco range starts in the target's own major: the release made for it (uSync 18.x '>= 18.0.0',
            // Contentment 7.x '[18.0.0, 19.0.0)'), not an older major whose range happens to reach it.
            async Task<bool> FloorInTargetMajor(NuGetVersion v)
            {
                var cf = await CandidateAsync(f.Id, v, target, candidates, ct);
                return cf is { Umbraco.Count: > 0 }
                       && cf.Umbraco.All(u => u.Range.MinVersion is { } min && min.Major == target.RangeVersion.Major);
            }

            return await UpgradeSelector.SelectAsync(f.Version, f.AllVersions, MetadataClears, FullyClears, o.AllowPrerelease,
                [FloorInTargetMajor, FitsTargetTfm]);
        }

        if (table.Row == 1)
        {
            choice = await WalkAsync();
            var withClearing = f with { ClearingVersions = clears.Where(kv => kv.Value).Select(kv => kv.Key).Order().ToList() };
            evidence = [.. evidence, .. _h15.Evaluate(withClearing, target)];
            recommendedDeprecated = choice.Recommended is { } r && candidates.GetValueOrDefault(r)?.Details.Deprecation is not null;
            // Review M3 (Q2): an upgrade to a version nobody could open rests on its ranges alone; say so, and do not
            // claim high confidence for it.
            var notInspected = choice.Recommended is { } rec && uninspected.TryGetValue(rec, out _);
            if (notInspected)
                evidence = [.. evidence, new Evidence("inspect", EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.Nupkg,
                    $"recommended version {choice.Recommended!.ToNormalizedString()} was not inspected ({uninspected[choice.Recommended]}); its ranges allow the target")];
            verdict = WithChoice(verdict with
            {
                Verdict = choice.Recommended is null ? Verdict.NoRelease : Verdict.Upgrade,
                Confidence = notInspected ? Confidence.Medium : Confidence.High,
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
