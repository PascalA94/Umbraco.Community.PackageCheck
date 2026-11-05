using PackageCheck.Core;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Inputs;
using PackageCheck.Report;
using PackageCheck.Report.Model;

namespace PackageCheck.Cli.Commands;

/// <summary>
/// Runs a scan: reads every path as a site, resolves the target per site, judges each package and builds the report.
/// Packages are judged eight at a time across all sites (design 7.3); the shared HTTP policy also caps requests at eight.
/// </summary>
public sealed class ScanOrchestrator(IProjectReader reader, IOverrideSource overrides, SiteServicesFactory sites, RunWarnings warnings, TimeProvider clock)
{
    public const int MaxDegreeOfParallelism = 8;

    public async Task<ScanReport> RunAsync(ScanRequest req, CancellationToken ct)
    {
        var paths = PathSet.Normalise(req.Paths.Count == 0 ? ["."] : req.Paths);
        // Design 6.1 (Pascal, 2026-10-30): a missing path is a usage error before anything is scanned.
        foreach (var p in paths)
            if (!File.Exists(p) && !Directory.Exists(p)) throw new PathNotFoundException(p);
        var privateIds = req.PrivateIdsPath is null ? new HashSet<string>() : PrivateIds.Load(req.PrivateIdsPath);

        var inputs = new List<ScanInput>();
        var runWarnings = new List<string>();
        foreach (var p in paths)
        {
            try
            {
                inputs.Add(await reader.ReadAsync(p, new InputOptions(req.Transitive, req.UseMsBuild), ct));
            }
            catch (NoUmbracoProjectException e) when (paths.Count > 1)
            {
                runWarnings.Add($"{e.Message}; skipped");
            }
        }
        if (inputs.Count == 0) throw new NoUmbracoProjectException(string.Join(", ", paths));

        var overrideSet = await overrides.LoadAsync(ct);

        var siteWork = new List<(ScanInput Input, TargetVersion? Target, UnknownReason? TargetFailure, SiteServices Services, IReadOnlyList<SitePackage> Packages)>();
        foreach (var input in inputs)
        {
            var services = await sites.ForAsync(input.SitePath, overrideSet);
            // The CMS packages are what is being upgraded, not packages to judge against it.
            var packages = SitePackages.From(input).Where(p => !FactsCollector.IsCms(p.Ref.Id)).ToList();
            try
            {
                var target = await services.Resolver.ResolveAsync(req.Target, input.SiteUmbracoVersion, req.Options.AllowPrerelease, ct);
                siteWork.Add((input, target, null, services, packages));
            }
            catch (TargetUnavailableException e)
            {
                // Plan Task 28b (P12): a feed that did not answer is a source failure, so the site is still reported,
                // every package unknown for that reason, and CI fails closed with exit 3.
                var warning = $"target could not be resolved: {Describe(e.Reason)}";
                siteWork.Add((input with { Warnings = [.. input.Warnings, warning] }, null, e.Reason, services, packages));
            }
        }

        var items = siteWork.SelectMany((s, site) => s.Packages.Select((p, i) => (Site: site, Index: i, Package: p))).ToList();
        var results = siteWork.Select(s => new PackageVerdict[s.Packages.Count]).ToArray();
        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = ct },
            async (item, token) =>
            {
                var work = siteWork[item.Site];
                results[item.Site][item.Index] = privateIds.Contains(item.Package.Ref.Id)
                    ? Unknown(item.Package, UnknownReason.PrivateFeed, "private-ids", "listed in --private-ids, so it was not looked up")
                    : work.Target is { } target
                        ? await work.Services.Engine.EvaluateAsync(item.Package, target, req.Options, token)
                        : Unknown(item.Package, work.TargetFailure!.Value, "target", $"not judged: the target {req.Target} could not be resolved");
            });

        var siteResults = siteWork.Select((s, i) => new SiteResult(
            s.Input, s.Target, results[i], Incomplete: s.TargetFailure is { } f && f != UnknownReason.Offline, RequestedTarget: req.Target)).ToList();
        return ReportBuilder.Build(
            siteResults,
            [.. runWarnings, .. warnings.Items],
            new ReportOptions(req.FailOn, req.Offline, req.Transitive, req.Options.Inspect, req.Options.AllowPrerelease),
            clock);
    }

    /// <summary>
    /// Review M4 F1: a target that could not be resolved leaves the whole site unjudged. Only an offline miss is an
    /// expected gap (preflight 1.34); a feed that refused (private feed), lacked Umbraco.Cms (not found) or failed makes
    /// the run incomplete, so <c>--fail-on</c> exits 3 rather than a silent green.
    /// </summary>
    private static string Describe(UnknownReason reason) => reason switch
    {
        UnknownReason.Offline => "offline",
        UnknownReason.PrivateFeed => "private feed",
        UnknownReason.NotFound => "not found",
        _ => "source error",
    };

    private static PackageVerdict Unknown(SitePackage pkg, UnknownReason reason, string source, string detail) =>
        new(pkg, Verdict.Unknown, null, reason, null, null, null,
            [new Evidence("source", EvidenceResult.Info, EvidenceWeight.Info, source, detail)], null, null);
}
