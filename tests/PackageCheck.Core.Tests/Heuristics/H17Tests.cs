using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Verdicts;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Heuristics;

public class H17Tests
{
    private static InspectionResult Ins(string? basePath) =>
        new("Our.Umbraco.Community.Contentment", "4.7.0", false, basePath, false, false, false, true, ["net10.0"], "hash", []);

    private static PackageFacts Facts(string? basePath, params string[] folders) =>
        HeuristicFacts.Pkg("Contentment", "1.0.0", "[17.0.0, 18.0.0)") with { Own = Ins(basePath), ProjectAppPlugins = folders };

    private static List<Evidence> Run(PackageFacts f) => new H17LeftoverAppPlugins().Evaluate(f, Target("17.7.2")).ToList();

    [Fact]
    public void NameMatch_Warns()
    {
        var e = Assert.Single(Run(Facts("/App_Plugins/Contentment", "App_Plugins/contentment")));
        Assert.Equal(("H17", EvidenceResult.Negative, EvidenceWeight.Medium), (e.Check, e.Result, e.Weight));
        Assert.Contains("App_Plugins/contentment", e.Detail);
    }

    [Fact]
    public void NoNameMatch_Silent() => Assert.Empty(Run(Facts("/App_Plugins/Contentment", "App_Plugins/Other")));

    [Fact]
    public void NoBasePath_Silent() => Assert.Empty(Run(Facts(null, "App_Plugins/contentment")));

    [Fact]
    public void WebRootBasePath_Silent() => Assert.Empty(Run(Facts("/", "App_Plugins/contentment")));

    [Fact]
    public void WwwrootAppPlugins_AlsoChecked() =>
        Assert.Single(Run(Facts("App_Plugins/Contentment", "wwwroot/App_Plugins/Contentment")));

    [Fact]
    public void NeverChangesVerdict()
    {
        var without = Facts("/App_Plugins/Contentment");
        var with = Facts("/App_Plugins/Contentment", "App_Plugins/contentment");
        var set = HeuristicSet.All(null);
        TableOutcome Decide(PackageFacts f) => TruthTable.Decide(set.SelectMany(h => h.Evaluate(f, Target("17.7.2"))).ToList(), f);
        Assert.Contains(set.SelectMany(h => h.Evaluate(with, Target("17.7.2"))), e => e.Check == "H17");
        Assert.Equal(Decide(without), Decide(with));
    }

    [Fact]
    public void ProjectFileSystem_ListsBothFolders_ReadOnly()
    {
        var dir = Directory.CreateTempSubdirectory("pc-h17-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "App_Plugins", "contentment"));
        Directory.CreateDirectory(Path.Combine(dir, "wwwroot", "App_Plugins", "Other"));
        File.WriteAllText(Path.Combine(dir, "App_Plugins", "file.txt"), "not a folder");
        var before = Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order().ToList();

        var folders = new ProjectFileSystem().AppPluginsFolders(dir);

        Assert.Equal(["App_Plugins/contentment", "wwwroot/App_Plugins/Other"], folders.Order(StringComparer.Ordinal));
        Assert.Equal(before, Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order().ToList());
        Assert.Empty(new ProjectFileSystem().AppPluginsFolders(Path.Combine(dir, "missing")));
    }

    [Fact]
    public async Task Collector_FillsFoldersFromProjectDirectories()
    {
        var dir = Directory.CreateTempSubdirectory("pc-h17-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "App_Plugins", "Contentment"));
        var feed = new FakeFeed().Add("Pkg", "1.0.0", "Umbraco.Cms.Core [17.0.0, 18.0.0)");
        var f = await new FactsCollector(feed, projectFs: new ProjectFileSystem())
            .CollectAsync("Pkg", NuGetVersion.Parse("1.0.0"), Target("17.7.2"), new FactsOptions(false, [dir]), CancellationToken.None);
        Assert.Equal(["App_Plugins/Contentment"], f.ProjectAppPlugins);
    }

    [Fact]
    public void SitePackages_CarryProjectDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "site");
        var input = new ScanInput(root, [new ProjectInfo(Path.Combine(root, "Web", "Web.csproj"), ["net10.0"], null,
            [new PackageRef("Pkg", "1.0.0", NuGetVersion.Parse("1.0.0"), true, null)])], [], default);
        var row = Assert.Single(SitePackages.From(input));
        Assert.Equal([Path.Combine(root, "Web")], row.ProjectDirectories);
    }
}
