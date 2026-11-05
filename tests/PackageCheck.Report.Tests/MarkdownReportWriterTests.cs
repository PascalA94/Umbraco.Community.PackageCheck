using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Report.Model;
using static PackageCheck.Report.Tests.ReportSamples;

namespace PackageCheck.Report.Tests;

public class MarkdownReportWriterTests
{
    // Snapshots must not change with every release, so the tool version is pinned.
    private static ScanReport Pin(ScanReport r) => r with { Tool = r.Tool with { Version = "0.0.0-test" } };

    private static ScanReport SingleSiteReport() => Pin(Build(
        [Site(
            Verdict("Our.Old", Core.Model.Verdict.NoRelease, Confidence.High,
                evidence: [new Evidence("H02", EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.NuGet, "range [13.0.0, 14.0.0) excludes 17.7.2")]),
            Verdict("Skybrud.Thing", Core.Model.Verdict.Upgrade, Confidence.High, recommended: "17.0.5", latestStable: "18.0.0"),
            Verdict("Astroboard", Core.Model.Verdict.NoRelease, overrideApplied: Override(Core.Model.Verdict.Suspicious, stale: true)),
            Verdict("Tom's #1 & Co", Core.Model.Verdict.Suspicious, Confidence.Medium,
                evidence:
                [
                    new Evidence("H03", EvidenceResult.Negative, EvidenceWeight.Low, EvidenceSources.NuGet, "open range >= 13.8.1"),
                    new Evidence("H08", EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.Nupkg, "legacy package.manifest only"),
                ]),
            Verdict("Private.Pkg", Core.Model.Verdict.Unknown, reason: UnknownReason.PrivateFeed),
            Verdict("Umbraco.Forms", Core.Model.Verdict.Ok, Confidence.High),
            Verdict("Newtonsoft.Json", Core.Model.Verdict.NotUmbraco))],
        ["no Umbraco project found in /x; skipped"]));

    private static ScanReport ThreeSitesReport()
    {
        SiteResult S(string path, params PackageVerdict[] v) => new(Input(path), Target(), v, Incomplete: false);
        return Pin(Build(
        [
            S("/sites/A", Verdict("Shared.Pkg", Core.Model.Verdict.Ok), Verdict("Only.A", Core.Model.Verdict.Upgrade, recommended: "2.0.0")),
            S("/sites/B", Verdict("Shared.Pkg", Core.Model.Verdict.Suspicious, Confidence.Low)),
            S("/sites/C", Verdict("Shared.Pkg", Core.Model.Verdict.NoRelease), Verdict("Lib.NotUmbraco", Core.Model.Verdict.NotUmbraco)),
        ]));
    }

    [Fact]
    public void SingleSite_Snapshot() => Snapshot.Match(MarkdownReportWriter.Write(SingleSiteReport(), verbose: false), "md");

    [Fact]
    public void ThreeSites_HasMatrix_Snapshot()
    {
        var md = MarkdownReportWriter.Write(ThreeSitesReport(), verbose: false);
        Assert.Contains("| Package | A | B | C |", md, StringComparison.Ordinal);
        Snapshot.Match(md, "md");
    }

    [Fact]
    public void SingleSite_HasNoMatrix()
    {
        var md = MarkdownReportWriter.Write(SingleSiteReport(), verbose: false);

        Assert.DoesNotContain("| Package | ", md.Split('\n').FirstOrDefault(l => l.StartsWith("| Package | MySite", StringComparison.Ordinal)) ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("## Sites", md, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageIdWithMarkdownAndHtml_IsEscaped()
    {
        var md = MarkdownReportWriter.Write(Build([Site(Verdict("<script>alert(1)</script>*x*", Core.Model.Verdict.Suspicious, Confidence.Medium))]), verbose: false);

        Assert.DoesNotContain("<script", md, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*x*", md, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Escaping_DoesNotDoubleEscapeEntities()
    {
        // Ruling R17 / preflight D5: "'" must render as &#39;, never as the literal text "&\#39;".
        var md = MarkdownReportWriter.Write(Build([Site(Verdict("Tom's #1 & Co", Core.Model.Verdict.Ok))]), verbose: false);

        Assert.Contains("Tom&#39;s \\#1 &amp; Co", md, StringComparison.Ordinal);
        Assert.DoesNotContain("&\\#", md, StringComparison.Ordinal);
    }

    [Fact]
    public void OverrideSummaryInjection_IsEscaped()
    {
        var o = Override(Core.Model.Verdict.Suspicious, summary: "x](javascript:alert(1)) <img src=x onerror=alert(1)>\n# heading");
        var md = MarkdownReportWriter.Write(Build([Site(Verdict("Astroboard", Core.Model.Verdict.NoRelease, overrideApplied: o))]), verbose: false);

        Assert.DoesNotContain("](javascript:", md, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", md, StringComparison.Ordinal);
        Assert.DoesNotContain("\n# heading", md, StringComparison.Ordinal);
    }

    [Fact]
    public void NonHttpsLinks_AreNotLinked()
    {
        var v = Verdict("X", Core.Model.Verdict.Replaced, recommended: "Y",
            marketplace: new Core.Facts.MarketplaceInfo("javascript:alert(1)", true, "Y", false, null, null));

        var md = MarkdownReportWriter.Write(Build([Site(v)]), verbose: false);

        Assert.DoesNotContain("(javascript:", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Override_IsLabelled_WithLink_AndStale()
    {
        var md = MarkdownReportWriter.Write(Build([Site(
            Verdict("Astroboard", Core.Model.Verdict.NoRelease, overrideApplied: Override(Core.Model.Verdict.Suspicious, stale: true)),
            Verdict("Local.One", Core.Model.Verdict.Ok, overrideApplied: Override(Core.Model.Verdict.Unknown, OverrideStatus.Works, origin: OverrideOrigin.Local)))]),
            verbose: false);

        Assert.Contains("[override](https://github.com/example/compat/issues/1)", md, StringComparison.Ordinal);
        Assert.Contains("[local override](https://github.com/example/compat/issues/1)", md, StringComparison.Ordinal);
        Assert.Contains("stale", md, StringComparison.Ordinal);
        Assert.Contains("computed: suspicious", md, StringComparison.Ordinal);
        Assert.Contains("reported not working", md, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverUsesTheWordBroken()
    {
        foreach (var md in new[]
                 {
                     MarkdownReportWriter.Write(SingleSiteReport(), verbose: true),
                     MarkdownReportWriter.Write(ThreeSitesReport(), verbose: true),
                     MarkdownReportWriter.Write(EveryValue(), verbose: true),
                 })
        {
            Assert.DoesNotContain("broken", md, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NotUmbraco_HiddenUnlessVerbose()
    {
        var quiet = MarkdownReportWriter.Write(SingleSiteReport(), verbose: false);
        var verbose = MarkdownReportWriter.Write(SingleSiteReport(), verbose: true);

        Assert.DoesNotContain("Newtonsoft.Json", quiet, StringComparison.Ordinal);
        Assert.Contains("1 non-Umbraco package hidden", quiet, StringComparison.Ordinal);
        Assert.Contains("Newtonsoft.Json", verbose, StringComparison.Ordinal);
    }

    [Fact]
    public void NoRelease_MentionsPrerelease()
    {
        var v = Verdict("Pre.Only", Core.Model.Verdict.NoRelease) with { PrereleaseCompatible = "18.0.0-beta1" };

        var md = MarkdownReportWriter.Write(Build([Site(v)]), verbose: false);

        Assert.Contains(@"prerelease 18\.0\.0\-beta1", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_LinksUmbraco17Check()
    {
        var md = MarkdownReportWriter.Write(Build([Site()]), verbose: false);

        Assert.Contains("[Umbraco17Check](https://www.nuget.org/packages/Umbraco17Check)", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_NeutralisesMentions()
    {
        // Review M4 F10 / M4-S4: package text pasted into a PR comment must not @mention anyone.
        Assert.DoesNotContain("@", TextSafety.Markdown("ping @org/security-team"), StringComparison.Ordinal);
        Assert.DoesNotContain("@", TextSafety.Html("ping @org/security-team"), StringComparison.Ordinal);
    }

    [Fact]
    public void Text_KeepsValidAstralCharacters_DropsLoneSurrogates()
    {
        Assert.Contains("\U0001F680", TextSafety.Console("ship \U0001F680 it"), StringComparison.Ordinal);
        Assert.Contains("\U0001F680", TextSafety.Markdown("ship \U0001F680 it"), StringComparison.Ordinal);
        Assert.Equal("ab", TextSafety.Console("a\uD800b"));
    }

    [Theory]
    [InlineData("a\u001b[31mb", "a[31mb")]
    [InlineData("line1\nline2\r\nline3", "line1 line2 line3")]
    [InlineData("pipe|cell", "pipe\\|cell")]
    public void Markdown_StripsControlAndNewlines(string input, string expected) =>
        Assert.Equal(expected, TextSafety.Markdown(input).Replace("\\[", "[", StringComparison.Ordinal).Replace("\\]", "]", StringComparison.Ordinal));
}
