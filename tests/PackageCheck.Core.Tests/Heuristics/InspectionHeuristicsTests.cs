using NuGet.Frameworks;
using NuGet.Packaging;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Heuristics;

/// <summary>H06, H07, H08, H09 and H11 over hand-built inspection results (shapes from spikes S2 and S3).</summary>
public class InspectionHeuristicsTests
{
    private static InspectionResult Ins(string id = "Pkg", bool upj = false, bool legacy = false, bool reader = false,
        bool mgmt = false, bool js = false, string[]? tfms = null, LibScan[]? scans = null, string? basePath = null) =>
        new(id, "1.0.0", upj, basePath, legacy, reader, mgmt, js, tfms ?? ["net10.0"], "hash", [], null, scans);

    private static PackageFacts Facts(InspectionResult? own, InspectionResult[]? deps = null, InspectionResult[]? assetDeps = null,
        string[]? groups = null)
    {
        var f = HeuristicFacts.Pkg("Pkg", "1.0.0", "[17.0.0, 18.0.0)");
        var details = f.Details with
        {
            DependencyGroups = (groups ?? []).Select(g => new PackageDependencyGroup(NuGetFramework.Parse(g), [])).ToList(),
        };
        return f with { Details = details, Own = own, Dependencies = deps ?? [], AssetDependencies = assetDeps };
    }

    private static List<Evidence> Run(IHeuristic h, PackageFacts f, string target = "17.7.2") => h.Evaluate(f, Target(target)).ToList();

    [Fact]
    public void H08_DualMode_DoesNotFire() // Crumpled.RobotsTxt shape: both manifest files
    {
        var f = Facts(Ins(upj: true, legacy: true));
        Assert.Empty(Run(new H08LegacyManifestOnly(), f));
        Assert.Single(Run(new H07UmbracoPackageJson(), f), e => e.IsHighPositive);
    }

    [Fact]
    public void H08_LegacyOnly_Fires()
    {
        var e = Assert.Single(Run(new H08LegacyManifestOnly(), Facts(Ins(legacy: true, js: true))));
        Assert.True(e.IsHighNegative);
        Assert.Equal("legacy package.manifest only", e.Detail);
    }

    [Fact]
    public void H08_OwnReader_Suppresses() =>
        Assert.Empty(Run(new H08LegacyManifestOnly(), Facts(Ins(legacy: true, reader: true))));

    [Fact]
    public void H08_DependencyOnlyRegistration_DoesNotSuppress() // ruling R23: row 2, not a pass
    {
        var f = Facts(Ins(legacy: true), deps: [Ins("Dep", upj: true)]);
        Assert.Single(Run(new H08LegacyManifestOnly(), f));
        Assert.Single(Run(new H07UmbracoPackageJson(), f));
    }

    [Fact]
    public void H08_AssetOnlyDependencyLegacyManifest_CountsAsOwn() // ruling R29: Plumber.Backoffice
    {
        var f = Facts(Ins(), assetDeps: [Ins("Pkg.Backoffice", legacy: true, js: true)]);
        Assert.Single(Run(new H08LegacyManifestOnly(), f));
    }

    [Fact]
    public void H08_JsWithNoManifest_IsIndeterminate() => // S2 finding 1
        Assert.Empty(Run(new H08LegacyManifestOnly(), Facts(Ins(js: true))));

    [Fact]
    public void H07_InDependency_Counts() // Workflow + StaticAssets shape
    {
        var e = Assert.Single(Run(new H07UmbracoPackageJson(),
            Facts(Ins("Umbraco.Workflow"), deps: [Ins("Umbraco.Workflow.StaticAssets", upj: true, basePath: "App_Plugins/Umbraco.Workflow")])));
        Assert.True(e.IsHighPositive);
        Assert.Contains("Umbraco.Workflow.StaticAssets", e.Detail);
        Assert.Contains("App_Plugins/Umbraco.Workflow", e.Detail);
    }

    [Fact]
    public void H09_InDependency_Counts() // SimpleDashboards + jcdcdev.Umbraco.Core shape (design text)
    {
        var e = Assert.Single(Run(new H09ManifestReaderRef(), Facts(Ins("SimpleDashboards"), deps: [Ins("jcdcdev.Umbraco.Core", reader: true)])));
        Assert.True(e.IsHighPositive);
        Assert.Contains("jcdcdev.Umbraco.Core", e.Detail);
    }

    [Fact]
    public void H09_OnlyTheTargetTfmLibCounts() // ruling R23
    {
        var scans = new[] { new LibScan("net8.0", true, false), new LibScan("net10.0", false, false) };
        Assert.Empty(Run(new H09ManifestReaderRef(), Facts(Ins(reader: true, scans: scans))));
        Assert.Single(Run(new H09ManifestReaderRef(), Facts(Ins(reader: true, scans: [new LibScan("net8.0", true, false)]))));
    }

    [Fact]
    public void H06_Net8LibOnTargetNet10_FailsLow() // MediaWiz shape. Ruling R1: needs the target major's own TFM
    {
        var e = Assert.Single(Run(new H06TfmFit(), Facts(Ins(tfms: ["net8.0"]))));
        Assert.Equal((EvidenceResult.Negative, EvidenceWeight.Low), (e.Result, e.Weight));
        Assert.Equal("no lib for net10.0 (has net8.0)", e.Detail);
    }

    [Fact]
    public void H06_MatchingLib_IsInfo()
    {
        var e = Assert.Single(Run(new H06TfmFit(), Facts(Ins(tfms: ["net9.0", "net10.0"]))));
        Assert.Equal(EvidenceResult.Info, e.Result);
    }

    [Fact]
    public void H06_WithoutInspection_UsesDependencyGroups()
    {
        var e = Assert.Single(Run(new H06TfmFit(), Facts(null, groups: ["net8.0"])));
        Assert.Equal((EvidenceResult.Negative, EvidenceSources.NuGet), (e.Result, e.Source));
        Assert.Empty(Run(new H06TfmFit(), Facts(null))); // nothing known, nothing said
    }

    [Fact]
    public void H11_ManagementApiWithoutClient_FiresLow()
    {
        var e = Assert.Single(Run(new H11ManagementApiNoClient(), Facts(Ins(mgmt: true))));
        Assert.Equal((EvidenceResult.Negative, EvidenceWeight.Low), (e.Result, e.Weight));
        Assert.Empty(Run(new H11ManagementApiNoClient(), Facts(Ins(mgmt: true, js: true))));
        Assert.Empty(Run(new H11ManagementApiNoClient(), Facts(Ins(mgmt: true), deps: [Ins("Dep", upj: true)])));
    }

    [Fact]
    public void InspectionHeuristics_SkippedForTargetBelow14()
    {
        var f = Facts(Ins(upj: true, legacy: true, reader: true, mgmt: true), deps: [Ins("Dep", legacy: true)]);
        IHeuristic[] contents = [new H07UmbracoPackageJson(), new H08LegacyManifestOnly(), new H09ManifestReaderRef(), new H11ManagementApiNoClient()];
        foreach (var h in contents)
        {
            Assert.False(h.AppliesTo(Target("13.9.0", "net8.0")));
            Assert.Empty(Run(h, f, "13.9.0"));
        }
        Assert.True(new H06TfmFit().AppliesTo(Target("13.9.0", "net8.0"))); // H06 is not a new-backoffice check
    }

    [Fact]
    public void ContentsHeuristics_SilentWithoutInspection()
    {
        var f = Facts(null);
        Assert.Empty(new IHeuristic[] { new H07UmbracoPackageJson(), new H08LegacyManifestOnly(), new H09ManifestReaderRef(), new H11ManagementApiNoClient() }
            .SelectMany(h => Run(h, f)));
    }

    [Fact]
    public void UnparseableLibFolder_NeverThrows() // security review M3 (S3): ParseFolder throws on "portable-net45+win-8"
    {
        var bad = Ins(tfms: ["portable-net45+win-8"], reader: true, scans: [new LibScan("portable-net45+win-8", true, true)]);
        var f = Facts(bad);
        foreach (var h in HeuristicSet.All(null))
            Assert.Null(Record.Exception(() => h.Evaluate(f, Target("17.7.2")).ToList()));
        Assert.Empty(Run(new H09ManifestReaderRef(), f)); // an unreadable folder is not the target's lib group
    }
}

