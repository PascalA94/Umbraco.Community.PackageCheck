using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Report.Model;
using Spectre.Console.Testing;
using static PackageCheck.Report.Tests.ReportSamples;

namespace PackageCheck.Report.Tests;

public class ConsoleReportWriterTests
{
    private const string Payload = "pkg\u001b[31mred[/]\nnext";
    private const string InertPayload = "pkg[31mred[/] next";

    private static string Render(ScanReport r, bool verbose = false)
    {
        var console = new TestConsole().Width(250);
        ConsoleReportWriter.Write(r, console, verbose);
        return console.Output;
    }

    private static Evidence Ev(string detail, string check = "H08") =>
        new(check, EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.Nupkg, detail);

    [Fact]
    public void NoColor_EnvVar_DisablesColour()
    {
        var report = Build([Site(Verdict("A", Core.Model.Verdict.NoRelease), Verdict("B", Core.Model.Verdict.Ok))]);
        string Run(bool noColor)
        {
            var sw = new StringWriter();
            ConsoleReportWriter.Write(report, ReportConsole.Create(sw, noColor, ansi: true), verbose: false);
            return sw.ToString();
        }

        Assert.True(ReportConsole.NoColorRequested(name => name == "NO_COLOR" ? "1" : null));
        Assert.False(ReportConsole.NoColorRequested(name => name == "NO_COLOR" ? "" : null));
        Assert.False(ReportConsole.NoColorRequested(_ => null));
        Assert.Contains("\u001b[38;5;", Run(noColor: false), StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[38;5;", Run(noColor: true), StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[3", Run(noColor: true), StringComparison.Ordinal);
    }

    public static TheoryData<string> Sources() => new()
    {
        "id", "versionText", "evidenceDetail", "deprecationText", "marketplaceAlternate", "marketplaceUrl", "overrideSummary",
        "overrideReplacement", "siteWarning", "runWarning", "sitePath", "project",
        // Carried from the M3 review: nupkg-derived text reaches the console as evidence detail.
        "basePath", "entryName", "h17Folder",
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public void Console_PackageText_IsInert(string source)
    {
        var verdict = source switch
        {
            "id" => Verdict(Payload, Core.Model.Verdict.Suspicious),
            "versionText" => Verdict("A", Core.Model.Verdict.Unknown, reason: UnknownReason.UnresolvedProperty) with
            {
                Package = new SitePackage(new PackageRef("A", Payload, null, true, Payload), ["Web"]),
            },
            "evidenceDetail" => Verdict("A", Core.Model.Verdict.Suspicious, evidence: [Ev(Payload)]),
            "deprecationText" => Verdict("A", Core.Model.Verdict.Replaced, evidence: [Ev($"deprecated: {Payload}", "H14")]),
            "marketplaceAlternate" => Verdict("A", Core.Model.Verdict.Replaced, recommended: Payload,
                marketplace: new MarketplaceInfo("https://marketplace.umbraco.com/package/a", true, Payload, false, null, null)),
            "marketplaceUrl" => Verdict("A", Core.Model.Verdict.Replaced,
                marketplace: new MarketplaceInfo(Payload, true, "B", false, null, null)),
            "overrideSummary" => Verdict("A", Core.Model.Verdict.NoRelease, overrideApplied: Override(Core.Model.Verdict.Suspicious, summary: Payload)),
            "overrideReplacement" => Verdict("A", Core.Model.Verdict.NoRelease, overrideApplied: Override(Core.Model.Verdict.Suspicious, replacement: Payload)),
            "project" => Verdict("A", Core.Model.Verdict.Suspicious) with
            {
                Package = new SitePackage(new PackageRef("A", "1.0.0", NuGet.Versioning.NuGetVersion.Parse("1.0.0"), true, null), [Payload]),
            },
            "basePath" => Verdict("A", Core.Model.Verdict.Suspicious, evidence: [Ev($"BasePath {Payload}", "H07")]),
            "entryName" => Verdict("A", Core.Model.Verdict.Suspicious, evidence: [Ev($"entry {Payload} skipped", "H08")]),
            "h17Folder" => Verdict("A", Core.Model.Verdict.Ok, evidence: [Ev($"App_Plugins/{Payload} left over", "H17")]),
            _ => Verdict("A", Core.Model.Verdict.Suspicious),
        };
        var input = source switch
        {
            "siteWarning" => Input("/sites/MySite", Payload),
            "sitePath" => Input($"/sites/{Payload}"),
            _ => Input(),
        };
        var report = Build(
            [new SiteResult(input, Target(), [verdict], Incomplete: false), new SiteResult(Input("/sites/Second"), Target(), [], false)],
            source == "runWarning" ? [Payload] : []);

        var output = Render(report, verbose: true);

        Assert.DoesNotContain('\u001b', output);
        Assert.Contains(InertPayload, output, StringComparison.Ordinal);
    }

    [Fact]
    public void TopThreeActions_WorstFirst()
    {
        var report = Build([Site(
            Verdict("Fine", Core.Model.Verdict.Ok),
            Verdict("Sus", Core.Model.Verdict.Suspicious, Confidence.Medium),
            Verdict("Up", Core.Model.Verdict.Upgrade, recommended: "2.0.0"),
            Verdict("Rep", Core.Model.Verdict.Replaced, recommended: "New.Pkg"),
            Verdict("Gone", Core.Model.Verdict.NoRelease))]);

        var lines = Render(report).Split('\n').Select(l => l.Trim()).ToList();
        var start = lines.IndexOf("Top actions");

        Assert.True(start >= 0, "no 'Top actions' heading");
        var actions = lines.Skip(start + 1).Take(3).ToList();
        Assert.StartsWith("1. Gone 1.0.0 has no release for 17.7.2", actions[0], StringComparison.Ordinal);
        Assert.Equal("2. Move Up to 2.0.0", actions[1]);
        Assert.Equal("3. Replace Rep with New.Pkg", actions[2]);
        Assert.DoesNotContain(lines, l => l.Contains("Check Sus", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Fine", StringComparison.Ordinal) && l.StartsWith("4.", StringComparison.Ordinal));
    }

    [Fact]
    public void TopActions_SuspiciousPhrasing_AndOkAbsent()
    {
        var output = Render(Build([Site(Verdict("Fine", Core.Model.Verdict.Ok), Verdict("Sus", Core.Model.Verdict.Suspicious, Confidence.Medium))]));

        Assert.Contains("1. Check Sus (suspicious): see evidence", output, StringComparison.Ordinal);
        Assert.DoesNotContain("2.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbose_PrintsEveryEvidenceLine()
    {
        var evidence = Enumerable.Range(1, 5).Select(i => Ev($"evidence line {i}", $"H0{i}")).ToList();
        var report = Build([Site(Verdict("A", Core.Model.Verdict.Suspicious, evidence: evidence))]);

        var verbose = Render(report, verbose: true);
        var quiet = Render(report, verbose: false);

        Assert.Equal(5, verbose.Split('\n').Count(l => l.Contains("evidence line", StringComparison.Ordinal)));
        Assert.Equal(0, quiet.Split('\n').Count(l => l.Contains("evidence line", StringComparison.Ordinal)));
    }

    [Fact]
    public void Warnings_ArePrinted()
    {
        var report = Build([new SiteResult(Input("/sites/MySite", "project Old.csproj not found; skipped"), Target(), [], false)],
            ["no Umbraco project found in /x; skipped"]);

        var output = Render(report);

        Assert.Contains("project Old.csproj not found; skipped", output, StringComparison.Ordinal);
        Assert.Contains("no Umbraco project found in /x; skipped", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Override_ShowsLabelAndStatus_NeverBroken()
    {
        var output = Render(Build([Site(Verdict("Astroboard", Core.Model.Verdict.NoRelease,
            overrideApplied: Override(Core.Model.Verdict.Suspicious, OverrideStatus.Broken, stale: true)))]), verbose: true);

        Assert.Contains("override", output, StringComparison.Ordinal);
        Assert.Contains("reported not working", output, StringComparison.Ordinal);
        Assert.Contains("computed: suspicious", output, StringComparison.Ordinal);
        Assert.Contains("https://github.com/example/compat/issues/1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("broken", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotUmbraco_HiddenUnlessVerbose()
    {
        var report = Build([Site(Verdict("Newtonsoft.Json", Core.Model.Verdict.NotUmbraco), Verdict("A", Core.Model.Verdict.Ok))]);

        Assert.DoesNotContain("Newtonsoft.Json", Render(report), StringComparison.Ordinal);
        Assert.Contains("Newtonsoft.Json", Render(report, verbose: true), StringComparison.Ordinal);
    }
}
