using System.Globalization;
using PackageCheck.Core;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Report.Model;

namespace PackageCheck.Report;

/// <summary>Turns engine results into the report model (design 7.6). Pure: the clock is passed in.</summary>
public static class ReportBuilder
{
    /// <summary>The judged verdicts in schema order; <c>not-umbraco</c> is never counted.</summary>
    private static readonly Verdict[] CountedVerdicts =
        [Verdict.Ok, Verdict.Upgrade, Verdict.NoRelease, Verdict.Suspicious, Verdict.Replaced, Verdict.Unknown];

    public static ScanReport Build(IReadOnlyList<SiteResult> sites, IReadOnlyList<string> runWarnings, ReportOptions options, TimeProvider clock)
    {
        var siteReports = sites.Select(BuildSite).ToList();
        var allVerdicts = sites.SelectMany(s => s.Verdicts).ToList();

        var counts = CountedVerdicts.ToDictionary(v => v.ToWireValue(), v => allVerdicts.Count(x => x.Verdict == v));
        var transitive = TransitiveSummary(sites.Select(s => s.Input.Transitive));
        // Only a failed source makes a run incomplete (ruling R4); offline or private-feed unknowns are expected gaps.
        var incomplete = sites.Any(s => s.Incomplete)
            || transitive == "unavailable"
            || allVerdicts.Any(v => v.Verdict == Verdict.Unknown && v.Reason == UnknownReason.SourceError);

        return new ScanReport(
            ScanReport.CurrentSchemaVersion,
            new ToolReport(ToolInfo.Name, ToolInfo.Version),
            clock.GetUtcNow(),
            new OptionsReport(options.FailOn.ToWireValue(), options.Offline, options.Transitive, options.Inspect, options.Prerelease),
            siteReports,
            new ReportSummary(counts, incomplete, transitive),
            [.. runWarnings.Select(Redaction.Text)]);
    }

    private static string? TransitiveSummary(IEnumerable<TransitiveStatus> statuses)
    {
        var set = statuses.ToHashSet();
        if (set.Contains(TransitiveStatus.Unavailable)) return "unavailable";
        if (set.Contains(TransitiveStatus.FromMsBuild)) return "msbuild";
        if (set.Contains(TransitiveStatus.FromLockFile)) return "lock-file";
        return null;
    }

    private static SiteReport BuildSite(SiteResult site)
    {
        var target = site.Target is { } t
            ? new TargetReport(t.Requested, t.Resolved.ToNormalizedString(), t.Tfm.GetShortFolderName(), t.Prerelease)
            : new TargetReport(site.RequestedTarget ?? "", site.RequestedTarget ?? "", null, false);

        var baseDir = Directory.Exists(site.Input.SitePath) || !Path.HasExtension(site.Input.SitePath)
            ? site.Input.SitePath
            : Path.GetDirectoryName(site.Input.SitePath) ?? site.Input.SitePath;

        var projects = site.Input.Projects
            .Select(p => new ProjectReport(RelativePath(baseDir, p.Path), p.UmbracoVersion?.ToNormalizedString(), p.Tfms))
            .ToList();

        return new SiteReport(
            Redaction.Text(site.Input.SitePath),
            target,
            projects,
            [.. site.Verdicts.Select(v => BuildPackage(v, baseDir))],
            [.. site.Input.Warnings.Select(Redaction.Text)]);
    }

    private static string RelativePath(string baseDir, string path) =>
        Path.GetRelativePath(baseDir, path).Replace('\\', '/');

    private static PackageReport BuildPackage(PackageVerdict v, string baseDir)
    {
        var r = v.Package.Ref;
        // Design 7.6: site-relative project paths, the same form as sites[].projects[].path. Names only when a caller
        // (explain, tests) built the package without paths.
        IReadOnlyList<string> projects = v.Package.ProjectPaths is { Count: > 0 } paths
            ? [.. paths.Select(p => RelativePath(baseDir, p))]
            : v.Package.Projects;
        return new PackageReport(
            r.Id,
            r.Version?.ToNormalizedString() ?? r.VersionText,
            r.Direct,
            projects,
            v.Verdict.ToWireValue(),
            v.Confidence?.ToWireValue(),
            v.Reason?.ToWireValue(),
            v.Recommended,
            v.HighestCompatible,
            v.PrereleaseCompatible,
            v.LatestStable,
            v.LatestStableClears,
            [.. v.Evidence.Select(e => new EvidenceReport(e.Check, Wire(e.Result), Wire(e.Weight), e.Source, Redaction.Text(e.Detail)))],
            v.Marketplace is { } m ? new MarketplaceReport(Redaction.Text(m.Url), m.IsRetired, m.AlternatePackageId) : null,
            v.Override is { } o
                ? new OverrideReport(
                    o.Entry.Id,
                    o.Entry.Status.ToString().ToLowerInvariant(),
                    Redaction.Text(o.Entry.Summary),
                    Redaction.Text(o.Entry.Evidence.AbsoluteUri),
                    Date(o.Entry.VerifiedOn),
                    Date(o.Entry.ReviewBy),
                    o.Stale,
                    o.ComputedVerdict.ToWireValue(),
                    o.Label,
                    o.Entry.Replacement)
                : null);
    }

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Wire(EvidenceResult r) => r switch
    {
        EvidenceResult.Positive => "positive",
        EvidenceResult.Negative => "negative",
        EvidenceResult.Info => "info",
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, null),
    };

    private static string Wire(EvidenceWeight w) => w switch
    {
        EvidenceWeight.High => "high",
        EvidenceWeight.Medium => "medium",
        EvidenceWeight.Low => "low",
        EvidenceWeight.Info => "info",
        _ => throw new ArgumentOutOfRangeException(nameof(w), w, null),
    };
}
