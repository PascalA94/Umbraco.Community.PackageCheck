using PackageCheck.Core;

namespace PackageCheck.Report.Model;

/// <summary>
/// The report, shaped exactly as the JSON (design 7.6, <c>schemaVersion</c> 1). Enum-like fields hold their wire
/// strings, so the JSON, markdown and console writers cannot disagree on spelling. <paramref name="Warnings"/> holds
/// run-level warnings such as a skipped path (design 6.1).
/// </summary>
public sealed record ScanReport(
    int SchemaVersion,
    ToolReport Tool,
    DateTimeOffset GeneratedAt,
    OptionsReport Options,
    IReadOnlyList<SiteReport> Sites,
    ReportSummary Summary,
    IReadOnlyList<string> Warnings)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record ToolReport(string Name, string Version);

/// <summary>What the run was asked to do; <see cref="FailOn"/> is the wire value (none, suspicious, upgrade, no-release).</summary>
public sealed record OptionsReport(string FailOn, bool Offline, bool Transitive, bool Inspect, bool Prerelease);

/// <summary>The run switches as the CLI holds them; the builder turns them into <see cref="OptionsReport"/>.</summary>
public sealed record ReportOptions(FailOn FailOn, bool Offline, bool Transitive, bool Inspect, bool Prerelease);

public sealed record SiteReport(
    string Path,
    TargetReport Target,
    IReadOnlyList<ProjectReport> Projects,
    IReadOnlyList<PackageReport> Packages,
    IReadOnlyList<string> Warnings);

/// <summary>
/// <paramref name="Resolved"/> repeats <paramref name="Requested"/> and <paramref name="Tfm"/> is null when the target
/// could not be resolved (a feed outage, design 7.7; preflight 1.38).
/// </summary>
public sealed record TargetReport(string Requested, string Resolved, string? Tfm, bool Prerelease);

public sealed record ProjectReport(string Path, string? UmbracoVersion, IReadOnlyList<string> Tfms);

/// <summary>
/// One judged (id, version), with exactly the fields of design 7.6 plus the optional <c>latestStable</c> pair (ruling
/// R25: "7.2.2 does not support 17.7.2" beside an upgrade inside an older major).
/// </summary>
public sealed record PackageReport(
    string Id,
    string? Current,
    bool Direct,
    IReadOnlyList<string> Projects,
    string Verdict,
    string? Confidence,
    string? Reason,
    string? Recommended,
    string? HighestCompatible,
    string? PrereleaseCompatible,
    string? LatestStable,
    bool LatestStableClears,
    IReadOnlyList<EvidenceReport> Evidence,
    MarketplaceReport? Marketplace,
    OverrideReport? Override);

public sealed record EvidenceReport(string Check, string Result, string Weight, string Source, string Detail);

public sealed record MarketplaceReport(string Url, bool Retired, string? Alternate);

/// <summary>
/// An applied override. <see cref="PackageReport.Verdict"/> is the override's verdict; <paramref name="ComputedVerdict"/>
/// is the truth table's, so nothing is silent (design 7.4).
/// </summary>
public sealed record OverrideReport(
    string Id,
    string Status,
    string Summary,
    string Evidence,
    string VerifiedOn,
    string ReviewBy,
    bool Stale,
    string ComputedVerdict,
    string Label,
    string? Replacement);

/// <summary>
/// <paramref name="Counts"/> has the six judged verdicts in severity-independent schema order and excludes
/// <c>not-umbraco</c>. <paramref name="Transitive"/> is <c>unavailable</c> when <c>--transitive</c> had no lock file
/// (design 6.1), else <c>lock-file</c>, <c>msbuild</c> or null.
/// </summary>
public sealed record ReportSummary(IReadOnlyDictionary<string, int> Counts, bool Incomplete, string? Transitive);
