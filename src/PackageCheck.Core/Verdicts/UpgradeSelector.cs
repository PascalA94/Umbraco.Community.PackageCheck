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
/// version, so a site moves the shortest distance. <c>fullyClears</c> is the inspected check; it runs at most
/// <see cref="MaxInspections"/> times per package to bound downloads when contents disagree with ranges (docs/spikes.md).
/// </summary>
public static class UpgradeSelector
{
    public const int MaxInspections = 3;

    public static async Task<UpgradeChoice> SelectAsync(NuGetVersion current, IReadOnlyList<NuGetVersion> all,
        Func<NuGetVersion, Task<bool>> metadataClears, Func<NuGetVersion, Task<bool>> fullyClears, bool allowPrerelease)
    {
        var newer = all.Where(v => v > current).ToList();
        var candidates = newer.Where(v => allowPrerelease || !v.IsPrerelease).ToList();

        NuGetVersion? recommended = null;
        var inspections = 0;
        foreach (var major in candidates.GroupBy(v => v.Major).OrderBy(g => g.Key))
        {
            foreach (var v in major.OrderDescending())
            {
                if (!await metadataClears(v)) continue;
                if (await fullyClears(v)) { recommended = v; break; }
                if (++inspections == MaxInspections) break;
            }
            if (recommended is not null || inspections == MaxInspections) break;
        }

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
