using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Sources.Overrides;
using static PackageCheck.Golden.Tests.Engine;

namespace PackageCheck.Golden.Tests;

/// <summary>
/// Real packages pinned by hash (design 6.1 and 8 ACs, docs/spikes.md "M0 gate"). Every test proves the computed verdict
/// with no overrides, except the two named for the seed (Pascal, 2026-10-30). Target 17.7.2 unless stated.
/// </summary>
[Trait("Category", "Golden")]
public class GoldenTests
{
    private static readonly ScanOptions Inspect = Options([]);

    [Fact]
    public async Task Astroboard_Suspicious_WithEvidence()
    {
        var v = await Computed.EvaluateAsync(Pkg("astroboard", "1.1.3"), Target("17.7.2"), Inspect);
        Assert.Equal(Verdict.Suspicious, v.Verdict);
        Assert.Null(v.Override);
        Assert.Contains(v.Evidence, e => e.Detail == "open range >= 13.8.1");
        Assert.Contains(v.Evidence, e => e.Detail == "legacy package.manifest only");
    }

    [Fact]
    public async Task Astroboard_SeedOverride_EndToEnd()
    {
        var seed = await new BundledOverrideSource().LoadAsync(TestContext.Current.CancellationToken);
        var v = await Create(seed).EvaluateAsync(Pkg("astroboard", "1.1.3"), Target("17.7.2"), Inspect);
        Assert.Equal(Verdict.NoRelease, v.Verdict);
        Assert.Equal("override", v.Override!.Label);
        Assert.Equal(Verdict.Suspicious, v.Override.ComputedVerdict);
    }

    [Fact]
    public async Task OurContentment_SeedOverride_IsReplaced() // rulings R27 and R30: the rename leg comes from the seed
    {
        var seed = await new BundledOverrideSource().LoadAsync(TestContext.Current.CancellationToken);
        var v = await Create(seed).EvaluateAsync(Pkg("Our.Umbraco.Community.Contentment", "4.7.0"), Target("17.7.2"), Inspect);
        Assert.Equal((Verdict.Replaced, "Umbraco.Community.Contentment"), (v.Verdict, v.Recommended));
        Assert.Equal(Verdict.NoRelease, v.Override!.ComputedVerdict);
    }

    [Fact]
    public async Task SimpleDashboards_Ok_High_ViaH09()
    {
        var v = await Computed.EvaluateAsync(Pkg("Umbraco.Community.SimpleDashboards", "17.2.0"), Target("17.7.2"), Inspect);
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.High), (v.Verdict, v.Confidence));
        Assert.Contains(v.Evidence, e => e.Check == "H09" && e.IsHighPositive);
    }

    [Fact]
    public async Task Workflow_Ok_ViaH07InStaticAssetsDependency()
    {
        var v = await Computed.EvaluateAsync(Pkg("Umbraco.Workflow", "17.5.4"), Target("17.7.2"), Inspect);
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.High), (v.Verdict, v.Confidence));
        Assert.Contains(v.Evidence, e => e.Check == "H07" && e.Detail.Contains("Umbraco.Workflow.StaticAssets"));
    }

    [Fact]
    public async Task RobotsTxt_NotSuspiciousFromManifests()
    {
        var v = await Computed.EvaluateAsync(Pkg("Crumpled.RobotsTxt", "3.1.1"), Target("17.7.2"), Inspect);
        Assert.NotEqual(Verdict.Suspicious, v.Verdict);
        Assert.DoesNotContain(v.Evidence, e => e.Check == "H08");
    }

    // Ruling R19 (Pascal, 2026-11-05): 6.2.1 is ok at 17.7.2; the G6 upgrade case is 5.2.1 -> 6.3.4 (plan's Contentment621_UpgradeTo633).
    [Fact]
    public async Task Contentment621_Ok_High()
    {
        var v = await Computed.EvaluateAsync(Pkg("Umbraco.Community.Contentment", "6.2.1"), Target("17.7.2"), Inspect);
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.High), (v.Verdict, v.Confidence));
    }

    [Fact]
    public async Task Contentment521_UpgradeTo634()
    {
        var v = await Computed.EvaluateAsync(Pkg("Umbraco.Community.Contentment", "5.2.1"), Target("17.7.2"), Inspect);
        Assert.Equal((Verdict.Upgrade, "6.3.4"), (v.Verdict, v.Recommended));
    }

    public static TheoryData<string, string, string, string, string> RecordedExpectations()
    {
        var data = new TheoryData<string, string, string, string, string>();
        foreach (var p in GoldenNupkgCache.All.Where(p => p.Computed is not null))
            data.Add(p.Id, p.Version, p.Target!, string.Join(' ', p.Options ?? []), Describe(p.Computed!));
        return data;
    }

    /// <summary>Task 7 Step 2 list: the computed verdict the M0 gate printed, now from the production engine.</summary>
    [Theory, MemberData(nameof(RecordedExpectations))]
    public async Task Recorded(string id, string version, string target, string options, string expected)
    {
        var v = await Computed.EvaluateAsync(Pkg(id, version), Target(target), Options(options.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        // The M0 gate printed "latest stable" only on upgrade lines; the engine also carries it on no-release rows (Contentment
        // 7.2.1 -> 7.2.2, "does not support"). It is display data, so it is compared only where the golden names one.
        var latestStable = expected.Contains("/ls=/", StringComparison.Ordinal) ? null : v.LatestStable;
        var actual = Describe(new GoldenExpectation(Wire(v.Verdict), v.Confidence?.ToString().ToLowerInvariant(), v.Recommended,
            v.HighestCompatible, latestStable, v.PrereleaseCompatible, null));
        Assert.True(expected == actual, $"{id} {version} {options}: expected {expected}, got {actual}; evidence: "
            + string.Join("; ", v.Evidence.Select(e => $"{e.Check} {e.Result} {e.Weight} {e.Detail}")));
    }

    [Fact]
    public void ExpectedMatchesComputed_ExceptSeedOverrides()
    {
        foreach (var p in GoldenNupkgCache.All.Where(p => p.Expected is not null && p.Computed is not null))
        {
            if (p.Id.StartsWith("Our.", StringComparison.Ordinal)) continue; // OurContentment_SeedOverride_IsReplaced
            Assert.Equal(Describe(p.Expected!), Describe(p.Computed! with { Row = null }));
        }
    }

    private static string Describe(GoldenExpectation e) =>
        $"{e.Verdict}/{e.Confidence}/rec={e.Recommended}/hc={e.HighestCompatible}/ls={e.LatestStable}/pc={e.PrereleaseCompatible}";

    private static string Wire(Verdict v) => v switch
    {
        Verdict.Ok => "ok",
        Verdict.Upgrade => "upgrade",
        Verdict.NoRelease => "no-release",
        Verdict.Suspicious => "suspicious",
        Verdict.Replaced => "replaced",
        Verdict.Unknown => "unknown",
        Verdict.NotUmbraco => "not-umbraco",
        _ => v.ToString(),
    };
}
