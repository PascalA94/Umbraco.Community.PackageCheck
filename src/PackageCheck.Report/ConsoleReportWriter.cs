using PackageCheck.Report.Model;
using Spectre.Console;
using static PackageCheck.Report.TextSafety;

namespace PackageCheck.Report;

/// <summary>
/// The console report (design 7.6): the markdown tables, coloured, plus the top three actions. Every package-controlled
/// string goes through <see cref="TextSafety.Console"/>, so ANSI sequences, Spectre markup and line breaks in package
/// data print as inert text on one line (Review Focus 1). Layout is not versioned.
/// </summary>
public static class ConsoleReportWriter
{
    public static void Write(ScanReport r, IAnsiConsole console, bool verbose)
    {
        console.MarkupLine($"[bold]Umbraco Package Check[/] {Console($"{r.Tool.Version}")}");
        if (r.Sites.Count == 1) console.MarkupLine($"Target {Console(ReportText.Target(r.Sites[0].Target))}");
        console.MarkupLine(Console(ReportText.Summary(r.Summary)));
        if (r.Summary.Incomplete)
            console.MarkupLine("[red]Incomplete:[/] a required source failed or transitive packages were unavailable.");

        foreach (var w in r.Warnings.Concat(r.Sites.SelectMany(s => s.Warnings)))
            console.MarkupLine($"[yellow]Warning:[/] {Console(w)}");

        foreach (var site in r.Sites) WriteSite(console, site, r.Sites.Count > 1, verbose);

        WriteActions(console, r);
        if (!verbose) console.MarkupLine("[grey]Use --verbose for the evidence behind each verdict.[/]");
    }

    private static bool Shown(PackageReport p, bool verbose) => verbose || p.Verdict != "not-umbraco";

    private static string Colour(string verdict) => verdict switch
    {
        "no-release" => "red",
        "upgrade" or "replaced" => "yellow",
        "suspicious" => "darkorange",
        "ok" => "green",
        _ => "grey",
    };

    private static void WriteSite(IAnsiConsole console, SiteReport site, bool multi, bool verbose)
    {
        console.WriteLine();
        if (multi)
        {
            console.Write(new Rule(Console(site.Path)) { Justification = Justify.Left });
            console.MarkupLine($"Target {Console(ReportText.Target(site.Target))}");
        }

        foreach (var (title, verdicts) in ReportText.Groups)
        {
            var rows = site.Packages.Where(p => verdicts.Contains(p.Verdict) && Shown(p, verbose)).ToList();
            if (rows.Count == 0) continue;
            var colour = Colour(verdicts[0]);
            var table = new Table { Title = new TableTitle($"[{colour}]{title} ({rows.Count})[/]"), Border = TableBorder.Rounded };
            table.AddColumns("Package", "Current", "Recommended", "Confidence", "Projects", "Note");
            foreach (var p in rows)
            {
                table.AddRow(
                    $"[{colour}]{Console(p.Id)}[/]",
                    Console(p.Current ?? ""),
                    Console(p.Recommended ?? ""),
                    p.Confidence ?? "n/a",
                    Console(ReportText.Projects(p.Projects)),
                    Console(Note(p, site.Target)));
            }
            console.Write(table);
        }

        var hidden = site.Packages.Count(p => !Shown(p, verbose));
        if (hidden > 0)
            console.MarkupLine($"[grey]{ReportText.Plural(hidden, "non-Umbraco package", "non-Umbraco packages")} hidden (use --verbose to show).[/]");

        if (!verbose) return;
        foreach (var p in site.Packages.Where(p => Shown(p, verbose)).OrderBy(p => ReportText.Rank(p.Verdict)))
        {
            console.MarkupLine($"[bold]{Console($"{p.Id} {p.Current}")}[/]: [{Colour(p.Verdict)}]{p.Verdict}[/]");
            foreach (var e in p.Evidence)
                console.MarkupLine($"  {Console($"{e.Check} {e.Result}, {e.Weight} ({e.Source}): {e.Detail}")}");
            if (p.Override is { } o)
            {
                console.MarkupLine($"  {Console($"{o.Label}: {ReportText.Status(o.Status)}: {o.Summary} ({o.Evidence}; review by {o.ReviewBy}{(o.Stale ? ", stale" : "")})")}");
                if (o.Replacement is not null) console.MarkupLine($"  {Console($"Suggested replacement: {o.Replacement}")}");
            }
            if (p.Marketplace is { } m)
                console.MarkupLine($"  {Console($"Marketplace: {m.Url}{(m.Retired ? " (retired)" : "")}{(m.Alternate is null ? "" : $", alternate {m.Alternate}")}")}");
        }
    }

    /// <summary>Same notes as the markdown table, as plain text (URLs printed, not linked).</summary>
    private static string Note(PackageReport p, TargetReport target)
    {
        var parts = new List<string>();
        if (p.Reason is not null) parts.Add(p.Reason);
        if (p.Verdict == "replaced" && p.Recommended is not null) parts.Add($"replaced by {p.Recommended}");
        if (p.Verdict == "no-release" && p.PrereleaseCompatible is not null) parts.Add($"prerelease {p.PrereleaseCompatible} supports the target");
        if (p.LatestStable is not null && !p.LatestStableClears && p.LatestStable != p.Recommended)
            parts.Add($"latest {p.LatestStable} does not support {target.Resolved}");
        if (p.Override is { } o)
            parts.Add($"{o.Label}: {ReportText.Status(o.Status)}, computed: {o.ComputedVerdict}{(o.Stale ? ", stale" : "")}");
        return string.Join("; ", parts);
    }

    /// <summary>The three worst packages, by the <c>--fail-on</c> severity order (design 6.3); ok, unknown and not-umbraco never appear.</summary>
    private static void WriteActions(IAnsiConsole console, ScanReport r)
    {
        var actions = r.Sites
            .SelectMany(s => s.Packages.Select(p => (Site: s, Package: p)))
            .Where(x => x.Package.Verdict is "no-release" or "upgrade" or "replaced" or "suspicious")
            .OrderBy(x => ReportText.Rank(x.Package.Verdict))
            .Take(3)
            .Select(x => Action(x.Package, x.Site.Target))
            .ToList();
        if (actions.Count == 0) return;

        console.WriteLine();
        console.MarkupLine("[bold]Top actions[/]");
        for (var i = 0; i < actions.Count; i++) console.MarkupLine($"{i + 1}. {Console(actions[i])}");
    }

    private static string Action(PackageReport p, TargetReport target) => p.Verdict switch
    {
        "no-release" => $"{p.Id} {p.Current} has no release for {target.Resolved}: remove it, replace it or wait for a release",
        "upgrade" => $"Move {p.Id} to {p.Recommended}",
        "replaced" => $"Replace {p.Id} with {p.Recommended ?? "its successor"}",
        _ => $"Check {p.Id} ({p.Verdict}): see evidence",
    };
}
