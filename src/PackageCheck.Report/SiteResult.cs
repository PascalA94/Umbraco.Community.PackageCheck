using PackageCheck.Core.Model;
using PackageCheck.Core.Targets;

namespace PackageCheck.Report;

/// <summary>
/// One scanned site, as the orchestrator hands it to <see cref="ReportBuilder"/>. <paramref name="Target"/> is null when
/// the target could not be resolved; <paramref name="RequestedTarget"/> then gives the text the user typed (preflight 1.38).
/// </summary>
public sealed record SiteResult(
    ScanInput Input,
    TargetVersion? Target,
    IReadOnlyList<PackageVerdict> Verdicts,
    bool Incomplete,
    string? RequestedTarget = null);
