using NuGet.Versioning;

namespace PackageCheck.Core.Verdicts;

public sealed record UpgradeChoice(
    NuGetVersion? Recommended,
    NuGetVersion? HighestCompatible,
    NuGetVersion? PrereleaseCompatible,
    NuGetVersion? LatestStable = null,
    bool LatestStableClears = false);

/// <summary>
/// Picks the upgrade for a row-1 package (design 6.1, G6): the lowest package major that clears, at its highest
/// version, so a site moves the shortest distance; majors built for the target (range floor in its major, then its TFM) are preferred. <c>fullyClears</c> is the inspected check; it runs at most
/// <see cref="MaxInspections"/> times per package to bound downloads when contents disagree with ranges (docs/spikes.md).
/// </summary>
public static class UpgradeSelector
{
    public const int MaxInspections = 3;

    public static async Task<UpgradeChoice> SelectAsync(NuGetVersion current, IReadOnlyList<NuGetVersion> all,
        Func<NuGetVersion, Task<bool>> metadataClears, Func<NuGetVersion, Task<bool>> fullyClears, bool allowPrerelease,
        IReadOnlyList<Func<NuGetVersion, Task<bool>>>? preferences = null)
    {
        var newer = all.Where(v => v > current).ToList();
        var candidates = newer.Where(v => allowPrerelease || !v.IsPrerelease).ToList();

        NuGetVersion? recommended = null;
        var inspections = 0;
        // Each version is inspected at most once across all passes, and costs one unit of the budget (final review F1):
        // otherwise a preferred version whose contents fail is charged once per pass and starves the plain rule.
        var inspected = new Dictionary<NuGetVersion, bool>();
        async Task<bool?> Inspect(NuGetVersion v)
        {
            if (inspected.TryGetValue(v, out var known)) return known;
            if (inspections == MaxInspections) return null;
            inspections++;
            return inspected[v] = await fullyClears(v);
        }

        // Preference passes first, in order: each only takes majors whose highest clearing version meets it. An open or
        // over-broad range from an older major (uSync 14.x or 17.x, Contentment 6.1.x) can clear on metadata, but the
        // version built for the target is the safer move (Pascal, 2026-11-06). The last pass is the plain rule.
        async Task<NuGetVersion?> Walk(Func<NuGetVersion, Task<bool>>? preference)
        {
            foreach (var major in candidates.GroupBy(v => v.Major).OrderBy(g => g.Key))
            {
                foreach (var v in major.OrderDescending())
                {
                    if (!await metadataClears(v)) continue;
                    // Ranges and TFMs are per-major build choices in practice, so one miss rules out the rest of the major.
                    if (preference is not null && !await preference(v)) break;
                    var full = await Inspect(v);
                    if (full is null) return null;
                    if (full.Value) return v;
                }
            }
            return null;
        }

        foreach (var preference in preferences ?? [])
        {
            if (recommended is not null || inspections >= MaxInspections) break;
            recommended = await Walk(preference);
        }
        if (recommended is null && inspections < MaxInspections) recommended = await Walk(null);

        NuGetVersion? highest = null;
        foreach (var v in candidates.OrderDescending())
            if (await metadataClears(v)) { highest = v; break; }
        if (highest == recommended) highest = null;

        var latestStable = newer.Where(v => !v.IsPrerelease).Max();
        var latestClears = latestStable is not null && await metadataClears(latestStable);

        NuGetVersion? prereleaseCompatible = null;
        if (!allowPrerelease && recommended is null)
            foreach (var v in newer.Where(v => v.IsPrerelease).Order())
                if (await metadataClears(v)) { prereleaseCompatible = v; break; }

        return new UpgradeChoice(recommended, highest, prereleaseCompatible, latestStable, latestClears);
    }
}
