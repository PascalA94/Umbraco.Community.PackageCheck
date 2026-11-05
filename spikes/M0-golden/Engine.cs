using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;

namespace M0Golden;

sealed record Target(NuGetVersion Resolved, NuGetFramework Tfm)
{
    // Prerelease targets are compared at their release base (design 6.1, Pascal 2026-10-30): 19.0.0-beta1 counts as 19.0.0.
    public NuGetVersion RangeVersion { get; } = new(Resolved.Major, Resolved.Minor, Resolved.Patch);
    public int Major => Resolved.Major;
}

sealed record Evidence(string Check, string Result, string Weight, string Detail, string? Alternate = null)
{
    public bool IsHighPositive => Result == "positive" && Weight == "high";
    public bool IsHighNegative => Result == "negative" && Weight == "high";
    public override string ToString() => $"{Check} {Result}/{Weight}: {Detail}";
}

enum RangeKind { NoPath, Excludes, AllowsBounded, AllowsOpen }

sealed record CmsDep(string Via, string Id, VersionRange Range, int Depth);

sealed record Outcome(
    int Row,
    string Verdict,
    string? Confidence,
    string? Reason,
    string? Recommended,
    string? HighestCompatible,
    string? LatestStable,
    bool LatestStableClears,
    string? PrereleaseCompatible,
    IReadOnlyList<Evidence> Evidence,
    bool RecommendedIsDeprecated = false);

sealed record LineSpec(string Id, string Version, NuGetVersion? TargetOverride = null, bool NoInspect = false, bool Prerelease = false, string? Note = null);

/// <summary>
/// A hand-coded copy of design 7.4 (heuristics, combining rule, truth table) for the M0 gate. Not production code:
/// Task 17 builds the real engine. Rulings applied: R1 (H06), R3 (H04 depth), R21 (nupkg patterns, TFM group), R23 (H09).
/// </summary>
sealed class Engine(Feed feed, NupkgStore store)
{
    const int MaxDepth = 3;           // R3: a direct dependency is depth 1; walk to depth 3 (design 7.4 H04).
    const int NewBackofficeMajor = 14; // H03/H07/H08/H09/H11 apply only to targets 14+ (design 7.4 applicability).
    const int InspectionCap = 3;      // plan Task 17: at most 3 candidate inspections per package.
    static readonly string[] RemovedBackofficePackages = ["Umbraco.Cms.Web.BackOffice"]; // the one id design 7.4 H05 names.

    readonly Dictionary<string, Contents?> contents = new(StringComparer.OrdinalIgnoreCase);

    // Integrations packages share the Umbraco.Cms prefix but are add-ons, not the CMS itself.
    static bool IsCms(string id) =>
        id.Equals("Umbraco.Cms", StringComparison.OrdinalIgnoreCase)
        || (id.StartsWith("Umbraco.Cms.", StringComparison.OrdinalIgnoreCase) && !id.StartsWith("Umbraco.Cms.Integrations.", StringComparison.OrdinalIgnoreCase));

    static bool IsPlatform(string id) =>
        id.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("NETStandard", StringComparison.OrdinalIgnoreCase);

    // R21: dependency ranges come from the TFM group the target selects, never the union of groups.
    static PackageDependencyGroup? Group(PkgVersion p, NuGetFramework tfm) => NuGetFrameworkUtility.GetNearest(p.Groups, tfm, g => g.TargetFramework);

    static IEnumerable<NuGet.Packaging.Core.PackageDependency> Deps(PkgVersion p, NuGetFramework tfm) => Group(p, tfm)?.Packages ?? [];

    async Task<PkgVersion?> FindAsync(string id, NuGetVersion v) => (await feed.VersionsAsync(id)).FirstOrDefault(x => x.Version == v);

    /// <summary>NuGet's lowest-applicable rule; prereleases only when the range's lower bound is itself a prerelease.</summary>
    async Task<PkgVersion?> ResolveAsync(string id, VersionRange range)
    {
        var allowPre = range.MinVersion?.IsPrerelease ?? false;
        var all = (await feed.VersionsAsync(id)).Where(v => range.Satisfies(v.Version) && (allowPre || !v.Version.IsPrerelease)).ToList();
        return all.FirstOrDefault(v => v.Listed) ?? all.FirstOrDefault();
    }

    async Task<bool> ReachesCmsAsync(PkgVersion p, NuGetFramework tfm, int hops)
    {
        var deps = Deps(p, tfm).ToList();
        if (deps.Any(d => IsCms(d.Id))) return true;
        if (hops <= 1) return false;
        foreach (var d in deps.Where(d => !IsPlatform(d.Id)))
        {
            var r = await ResolveAsync(d.Id, d.VersionRange);
            if (r is not null && await ReachesCmsAsync(r, tfm, hops - 1)) return true;
        }
        return false;
    }

    /// <summary>
    /// Umbraco.Cms.* ranges (direct, or by the H04 walk when there is no direct one) and the Umbraco dependencies
    /// (non-core packages with their own path to Umbraco.Cms.*) whose contents count for H07/H09.
    /// </summary>
    async Task<(List<CmsDep> Cms, List<PkgVersion> UmbracoDeps)> WalkAsync(PkgVersion root, NuGetFramework tfm)
    {
        var cms = Deps(root, tfm).Where(d => IsCms(d.Id)).Select(d => new CmsDep(root.Id, d.Id, d.VersionRange, 1)).ToList();
        var walkForRange = cms.Count == 0;
        var umbracoDeps = new List<PkgVersion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id };
        var frontier = new List<(PkgVersion Pkg, int Depth)> { (root, 0) };
        while (frontier.Count > 0)
        {
            var next = new List<(PkgVersion, int)>();
            foreach (var (pkg, depth) in frontier)
            {
                if (depth >= MaxDepth) continue;
                foreach (var d in Deps(pkg, tfm))
                {
                    if (IsCms(d.Id))
                    {
                        if (walkForRange && depth > 0) cms.Add(new CmsDep(pkg.Id, d.Id, d.VersionRange, depth + 1));
                        continue;
                    }
                    if (IsPlatform(d.Id) || !seen.Add(d.Id)) continue;
                    // R34: r sits at depth+1, so its Umbraco.Cms.* dependency may be at most MaxDepth; pass what is left of the budget.
                    var remaining = MaxDepth - (depth + 1);
                    if (remaining < 1) continue;
                    var r = await ResolveAsync(d.Id, d.VersionRange);
                    if (r is null || !await ReachesCmsAsync(r, tfm, remaining)) continue;
                    umbracoDeps.Add(r);
                    next.Add((r, depth + 1));
                }
            }
            frontier = next;
        }
        return (cms, umbracoDeps);
    }

    async Task<Contents?> InspectAsync(PkgVersion p, NuGetFramework tfm)
    {
        var key = $"{p.Id}/{p.Version.ToNormalizedString()}";
        if (contents.TryGetValue(key, out var hit)) return hit;
        var path = await store.GetAsync(p.Id, p.Version);
        return contents[key] = path is null ? null : Inspector.Inspect(p.Id, p.Version.ToNormalizedString(), path, tfm);
    }

    public async Task<Outcome> EvaluateAsync(LineSpec line, Target target, bool marketplace = true)
    {
        var all = await feed.VersionsAsync(line.Id);
        var cur = all.FirstOrDefault(v => v.Version == NuGetVersion.Parse(line.Version))
                  ?? throw new InvalidOperationException($"{line.Id} {line.Version} not on the feed");
        var table = await DecideAsync(cur, target, inspect: !line.NoInspect, marketplace);
        if (table.Row != 1) return ApplyReplacement(table);

        // Row 1: upgrade if a newer version clears, else no-release (design 7.4 row 1; 6.1 "upgrade picks").
        var pick = await SelectUpgradeAsync(cur, all, target, inspect: !line.NoInspect, line.Prerelease);
        var ev = table.Evidence.ToList();
        if (pick.PrereleaseOnly is not null) ev.Add(new Evidence("H15", "info", "info", $"only a prerelease supports the target: {pick.PrereleaseOnly}"));
        var recommendedIsDeprecated = pick.Recommended is not null
            && all.First(v => v.Version == NuGetVersion.Parse(pick.Recommended)).Deprecation is not null;
        var outcome = table with
        {
            RecommendedIsDeprecated = recommendedIsDeprecated,
            Verdict = pick.Recommended is null ? "no-release" : "upgrade",
            Confidence = "high",
            Recommended = pick.Recommended,
            HighestCompatible = pick.HighestCompatible,
            LatestStable = pick.LatestStable,
            LatestStableClears = pick.LatestStableClears,
            PrereleaseCompatible = pick.PrereleaseCompatible,
            Evidence = ev,
        };
        return ApplyReplacement(outcome);
    }

    /// <summary>
    /// Ruling R27: H13/H14 turn the result into replaced (high), naming the alternate, except on rows 5-8 and on a row 1
    /// that resolves to an upgrade to a non-deprecated, non-retired version of the same id; there they add a note only.
    /// A row-1 no-release with high H13/H14 becomes replaced and keeps its range and H15 evidence lines.
    /// Ruling R32: never over unknown/unsafe-archive.
    /// </summary>
    static Outcome ApplyReplacement(Outcome o)
    {
        var repl = o.Evidence.FirstOrDefault(e => e.Check is "H13" or "H14" && e.IsHighNegative);
        if (repl is null || o.Row is >= 5 and <= 8 || o.Reason == "unsafe-archive") return o;
        // Marketplace retirement is per id, so an upgrade within a retired id does not escape it.
        var retiredId = o.Evidence.Any(e => e.Check == "H13" && e.IsHighNegative);
        if (o.Row == 1 && o.Verdict == "upgrade" && !o.RecommendedIsDeprecated && !retiredId) return o;
        // R34: the alternate id travels as data on the evidence, never parsed out of display text.
        return o with { Verdict = "replaced", Confidence = "high", Reason = null, Recommended = repl.Alternate };
    }

    sealed record UpgradePick(string? Recommended, string? HighestCompatible, string? LatestStable, bool LatestStableClears, string? PrereleaseCompatible, string? PrereleaseOnly);

    /// <summary>Plan Task 17's selection: lowest package major that clears, highest version in it; candidates are newer than current.</summary>
    async Task<UpgradePick> SelectUpgradeAsync(PkgVersion cur, IReadOnlyList<PkgVersion> all, Target target, bool inspect, bool allowPrerelease)
    {
        async Task<bool> MetadataClears(PkgVersion v) => (await DecideAsync(v, target, inspect: false, marketplace: false)).Row is >= 5 and <= 8;
        async Task<bool> FullyClears(PkgVersion v) => !inspect || (await DecideAsync(v, target, inspect: true, marketplace: false)).Row is >= 5 and <= 8;

        var newer = all.Where(v => v.Listed && v.Version > cur.Version).ToList();
        var candidates = newer.Where(v => allowPrerelease || !v.Version.IsPrerelease).ToList();
        string? recommended = null;
        var inspections = 0;
        foreach (var major in candidates.GroupBy(v => v.Version.Major).OrderBy(g => g.Key))
        {
            foreach (var v in major.OrderByDescending(v => v.Version))
            {
                if (!await MetadataClears(v)) continue;
                if (await FullyClears(v)) { recommended = v.Version.ToNormalizedString(); break; }
                if (++inspections == InspectionCap) break;
            }
            if (recommended is not null || inspections == InspectionCap) break;
        }

        string? highestCompatible = null;
        foreach (var v in candidates.OrderByDescending(v => v.Version))
            if (await MetadataClears(v)) { highestCompatible = v.Version.ToNormalizedString(); break; }
        if (highestCompatible == recommended) highestCompatible = null;

        var latestStable = newer.Where(v => !v.Version.IsPrerelease).MaxBy(v => v.Version);
        var latestClears = latestStable is not null && await MetadataClears(latestStable);

        var stableClears = false;
        foreach (var v in newer.Where(v => !v.Version.IsPrerelease)) if (await MetadataClears(v)) { stableClears = true; break; }
        string? lowestPreClearing = null;
        foreach (var v in newer.Where(v => v.Version.IsPrerelease).OrderBy(v => v.Version))
            if (await MetadataClears(v)) { lowestPreClearing = v.Version.ToNormalizedString(); break; }

        return new UpgradePick(
            recommended,
            highestCompatible,
            latestStable?.Version.ToNormalizedString(),
            latestClears,
            recommended is null && !allowPrerelease ? lowestPreClearing : null,
            stableClears ? null : (allowPrerelease ? recommended : lowestPreClearing));
    }

    /// <summary>Evidence plus the first matching truth-table row (design 7.4). Row 1 is resolved by the caller.</summary>
    async Task<Outcome> DecideAsync(PkgVersion cur, Target target, bool inspect, bool marketplace)
    {
        var ev = new List<Evidence>();
        var (cms, umbracoDeps) = await WalkAsync(cur, target.Tfm);
        Outcome Make(int row, string verdict, string? conf, string? reason = null) => new(row, verdict, conf, reason, null, null, null, false, null, ev);

        if (cms.Count == 0)
        {
            ev.Add(new Evidence("H04", "info", "info", $"no path to Umbraco.Cms.* within depth {MaxDepth}"));
            return Make(10, "not-umbraco", null);
        }

        // H01/H02 (H04 inherits them when the ranges come from the walk).
        var excluding = cms.Where(c => !c.Range.Satisfies(target.RangeVersion)).ToList();
        var open = cms.Where(c => !c.Range.HasUpperBound).ToList();
        var via = cms.Any(c => c.Depth > 1) ? $" (H04 via {string.Join(", ", cms.Select(c => c.Via).Distinct())})" : "";
        RangeKind kind;
        if (excluding.Count > 0)
        {
            kind = RangeKind.Excludes;
            foreach (var c in excluding) ev.Add(new Evidence("H02", "negative", "high", $"{c.Id} {c.Range.PrettyPrint()} excludes {target.Resolved}{via}"));
        }
        else
        {
            kind = open.Count > 0 ? RangeKind.AllowsOpen : RangeKind.AllowsBounded;
            var r = cms.Select(c => c.Range.PrettyPrint()).Distinct();
            ev.Add(new Evidence("H01", "positive", kind == RangeKind.AllowsOpen ? "low" : "medium", $"Umbraco.Cms.* {string.Join(" / ", r)} allows {target.Resolved}{via}"));
        }
        // R28: an open range with no floor counts as a floor below 14.
        var openFloorMajor = open.Count == 0 ? (int?)null : open.Min(c => c.Range.MinVersion?.Major ?? 0);
        var openMin = open.Select(c => c.Range.MinVersion).Where(m => m is not null).Min();
        var h03 = kind == RangeKind.AllowsOpen && openFloorMajor < NewBackofficeMajor && target.Major >= NewBackofficeMajor;
        if (h03) ev.Add(new Evidence("H03", "negative", "low", $"open range >= {openMin?.ToNormalizedString() ?? "(no floor)"}"));

        var removed = cms.Where(c => RemovedBackofficePackages.Contains(c.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        var h05 = removed.Count > 0 && target.Major >= NewBackofficeMajor;
        if (h05) ev.Add(new Evidence("H05", "negative", "high", $"depends on {removed[0].Id}, removed in the new backoffice"));

        if (cur.Deprecation is { } dep)
        {
            ev.Add(dep.AlternateId is null
                ? new Evidence("H14", "info", "info", $"deprecated on NuGet ({string.Join(", ", dep.Reasons)}), no alternate")
                : new Evidence("H14", "negative", "high", $"deprecated on NuGet ({string.Join(", ", dep.Reasons)}); alternate {dep.AlternateId}", dep.AlternateId));
        }

        if (marketplace)
        {
            var mp = await feed.MarketplaceAsync(cur.Id, target.Resolved);
            if (mp is null) ev.Add(new Evidence("H12", "info", "info", "not listed on the Marketplace"));
            else
            {
                if (mp.LatestForTarget is not null) ev.Add(new Evidence("H12", "info", "info", $"Marketplace latest for target: {mp.LatestForTarget}"));
                if (mp.IsRetired)
                    ev.Add(mp.AlternateId is null
                        ? new Evidence("H13", "info", "info", "retired on the Marketplace, no alternate")
                        : new Evidence("H13", "negative", "high", $"retired on the Marketplace; alternate {mp.AlternateId}", mp.AlternateId));
            }
        }

        // Contents (H06 from lib/, H07, H08, H09): only with inspection and a 14+ target.
        var contentsKnown = false;
        bool h07 = false, h08 = false, h09 = false, serverOnly = false;
        IReadOnlyList<NuGetFramework>? libTfms = null;
        if (inspect && target.Major >= NewBackofficeMajor)
        {
            var own = await InspectAsync(cur, target.Tfm);
            if (own is null) ev.Add(new Evidence("inspect", "info", "info", "nupkg over size cap; metadata only"));
            else if (own.UnsafeReason is not null)
            {
                ev.Add(new Evidence("inspect", "negative", "high", $"unsafe archive: {own.UnsafeReason}"));
                return Make(0, "unknown", null, "unsafe-archive");
            }
            else
            {
                contentsKnown = true;
                libTfms = own.LibTfms;
                var depContents = new List<Contents>();
                foreach (var d in umbracoDeps)
                {
                    var c = await InspectAsync(d, target.Tfm);
                    if (c is null || c.UnsafeReason is not null) continue;
                    depContents.Add(c);
                }
                var ownH07 = own.UmbracoPackageJson.Count > 0;
                var ownH09 = own.ManifestReaders.Count > 0;
                foreach (var c in new[] { own }.Concat(depContents))
                {
                    var who = c == own ? "own" : $"dependency {c.Id} {c.Version}";
                    if (c.UmbracoPackageJson.Count > 0)
                        ev.Add(new Evidence("H07", "positive", "high", $"{who}: {c.UmbracoPackageJson[0]}{(c.BasePaths.Count > 0 ? $", BasePath {c.BasePaths[0]}" : "")}"));
                    foreach (var r in c.ManifestReaders) ev.Add(new Evidence("H09", "positive", "high", $"{who}: {r} implements IPackageManifestReader"));
                }
                h07 = ev.Any(e => e.Check == "H07");
                h09 = ev.Any(e => e.Check == "H09");
                // R23: only the package's own H07/H09 suppress its own H08; dependency-only evidence leaves H08 standing (row 2).
                if (own.PackageManifest.Count > 0 && !ownH07 && !ownH09)
                {
                    h08 = true;
                    ev.Add(new Evidence("H08", "negative", "high", "legacy package.manifest only"));
                }
                else if (own.PackageManifest.Count > 0)
                    ev.Add(new Evidence("H08", "info", "info", "package.manifest ignored: the package also ships the new registration (dual mode)"));
                else if (own.JsCount > 0 && !ownH07 && !ownH09)
                    // R21: JS with neither manifest file is indeterminate for H08, never legacy on its own.
                    ev.Add(new Evidence("H08", "info", "info", $"{own.JsCount} backoffice JS file(s), no manifest file: indeterminate"));
                // R31: server-only needs no client assets AND no H07/H09 AND no Management API refs; it never discards H07/H09.
                serverOnly = !h07 && !h09 && !own.HasClientAssets && own.ManagementApiRefs == 0 && depContents.All(c => !c.HasClientAssets && c.ManagementApiRefs == 0);
                if (serverOnly) ev.Add(new Evidence("contents", "info", "info", "server-only package (no client assets, no H07/H09, no Management API refs)"));
            }
        }

        // H06 (R1): fails when no lib/ TFM is the target major's own TFM. Without inspection the nuspec groups stand in.
        var tfmSet = (libTfms is { Count: > 0 } ? libTfms : cur.Groups.Select(g => g.TargetFramework).Where(f => !f.IsAny && !f.IsAgnostic).ToList()).ToList();
        bool? h06Fails = tfmSet.Count == 0 ? null : !tfmSet.Any(f => f.Equals(target.Tfm));
        var tfmText = string.Join(", ", tfmSet.Select(f => f.GetShortFolderName()).Distinct());
        if (h06Fails == true) ev.Add(new Evidence("H06", "negative", "low", $"no {(libTfms is { Count: > 0 } ? "lib/" : "dependency-group")} TFM is {target.Tfm.GetShortFolderName()} (has {tfmText})"));
        else if (h06Fails == false) ev.Add(new Evidence("H06", "info", "info", $"{target.Tfm.GetShortFolderName()} present"));

        var highNeg = h05 || h08;
        var highPos = contentsKnown && (h07 || h09);
        var open14 = kind == RangeKind.AllowsOpen && !h03; // R28 row 8: open, H03 not applicable (floor 14+ or target below 14)

        // Design 7.4 truth table, first match wins. Row 1's upgrade/no-release split happens in EvaluateAsync.
        if (kind == RangeKind.Excludes) return Make(1, "upgrade", "high");
        if (highNeg && highPos) return Make(2, "unknown", null, "conflicting-evidence");
        if (kind == RangeKind.AllowsBounded && highNeg) return Make(3, "suspicious", "medium");
        if (kind == RangeKind.AllowsOpen && highNeg) return Make(4, "suspicious", "medium"); // R28: with or without H03
        if (kind == RangeKind.AllowsBounded && !highNeg && highPos) return Make(5, "ok", "high");
        if (kind == RangeKind.AllowsOpen && !highNeg && highPos) return Make(6, "ok", "high");
        if (kind == RangeKind.AllowsBounded && !highNeg) return Make(7, "ok", "medium");
        if (open14 && !highNeg) return Make(8, "ok", "low");
        if (h03 && !highNeg) return h06Fails == true ? Make(9, "suspicious", "low") : Make(9, "ok", "low");
        // R28 fallback: should be unreachable after the widening above; Task 17's coverage test proves it.
        ev.Add(new Evidence("table", "info", "info", "no truth-table row"));
        return Make(-1, "unknown", null, "conflicting-evidence");
    }
}
