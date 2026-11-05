using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Overrides;

namespace PackageCheck.Core.Model;

/// <summary>
/// The judged package. <see cref="LatestStable"/> and <see cref="LatestStableClears"/> go beyond the plan's record so the
/// report can show "7.2.2 does not support 17.7.2" next to an upgrade within an older major (ruling R25).
/// </summary>
public sealed record PackageVerdict(
    SitePackage Package,
    Verdict Verdict,
    Confidence? Confidence,
    UnknownReason? Reason,
    string? Recommended,
    string? HighestCompatible,
    string? PrereleaseCompatible,
    IReadOnlyList<Evidence> Evidence,
    MarketplaceInfo? Marketplace,
    OverrideApplied? Override,
    string? LatestStable = null,
    bool LatestStableClears = false);

/// <summary>Per-run switches the engine reads (CLI <c>--no-inspect</c>, <c>--prerelease</c>, <c>--verbose</c>).</summary>
public sealed record ScanOptions(bool Inspect, bool AllowPrerelease, bool Verbose);
