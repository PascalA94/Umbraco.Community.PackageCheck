using NuGet.Versioning;
using PackageCheck.Core.Model;

namespace PackageCheck.Inputs.Tests;

public class ProjectFileReaderTests
{
    // The walk-up stops at the fixture root so the repo's own Directory.Build.props never leaks into a fixture.
    private static ProjectInfo Read(string fixture, string csproj) =>
        ProjectFileReader.Read(Fixtures.Path($"{fixture}/{csproj}"), Fixtures.Path(fixture));

    private static PackageRef Pkg(ProjectInfo p, string id) => p.Packages.Single(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Cpm_VersionComesFromProps()
    {
        var p = Read("proj-cpm", "Web/Web.csproj");
        Assert.Equal(NuGetVersion.Parse("6.2.1"), Pkg(p, "Contentment").Version);
        Assert.True(Pkg(p, "Contentment").Direct);
    }

    [Fact]
    public void Cpm_VersionOverrideWins()
    {
        var p = Read("proj-cpm-override", "Web/Web.csproj");
        Assert.Equal(NuGetVersion.Parse("6.3.3"), Pkg(p, "Contentment").Version);
    }

    [Fact]
    public void Property_FromDirectoryBuildProps_IsExpanded()
    {
        // The property is defined two levels up and chained through a nearer props file.
        var p = Read("proj-props", "src/Web/Web.csproj");
        Assert.Equal(NuGetVersion.Parse("17.7.2"), Pkg(p, "Umbraco.Cms").Version);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), Pkg(p, "Our.Thing").Version);
        Assert.Equal("$(UmbracoVersion)", Pkg(p, "Umbraco.Cms").VersionText);
    }

    [Fact]
    public void UnresolvedProperty_IsUnknownNotCrash()
    {
        var p = Read("unresolved-prop", "Web/Web.csproj");
        var r = p.Packages.Single(x => x.Id == "Our.Thing");
        Assert.Null(r.Version);
        Assert.Equal("$(UmbracoVersion)", r.VersionText);
        Assert.StartsWith("unresolved MSBuild property", r.UnresolvedReason);
        Assert.Contains("$(UmbracoVersion)", r.UnresolvedReason);
    }

    [Fact]
    public void FloatingVersion_WithoutLock_IsUnresolved()
    {
        var r = Pkg(Read("proj-floating", "Web/Web.csproj"), "Umbraco.Cms");
        Assert.Null(r.Version);
        Assert.Equal("17.*", r.VersionText);
        Assert.StartsWith("unresolved MSBuild property", r.UnresolvedReason);
        Assert.Contains("17.*", r.UnresolvedReason);
    }

    [Fact]
    public void VersionAsChildElement_IsRead()
    {
        var r = Pkg(Read("proj-child-version", "Web/Web.csproj"), "Our.Thing");
        Assert.Equal(NuGetVersion.Parse("1.2.3"), r.Version);
        Assert.Equal("1.2.3", r.VersionText);
    }

    [Fact]
    public void MultiTargeting_TfmsListed()
    {
        Assert.Equal(new[] { "net8.0", "net10.0" }, Read("proj-multitarget", "Web/Web.csproj").Tfms);
    }

    [Fact]
    public void UmbracoVersion_IsHighestOfCmsAndCore()
    {
        var p = Read("proj-umbraco-highest", "Web/Web.csproj");
        Assert.Equal(NuGetVersion.Parse("17.7.2"), p.UmbracoVersion);
    }

    [Fact]
    public void PackageIds_CompareCaseInsensitively_AndKeepTheWrittenId()
    {
        // "umbraco.cms.core" at 16.0.0 is a case variant of Umbraco.Cms.Core: it is a duplicate, first spelling wins.
        var p = Read("proj-umbraco-highest", "Web/Web.csproj");
        Assert.Equal(2, p.Packages.Count);
        Assert.Contains(p.Packages, x => x.Id == "Umbraco.Cms.Core");
    }

    [Fact]
    public void MalformedCsproj_IsSkippedWithWarning()
    {
        var (info, warnings) = ProjectFileReader.ReadSafe(Fixtures.Path("proj-malformed/Web/Web.csproj"), Fixtures.Path("proj-malformed"));
        Assert.Null(info);
        var w = Assert.Single(warnings);
        Assert.Contains("Web.csproj", w);
    }

    [Fact]
    public void MissingCsproj_IsSkippedWithWarning()
    {
        var (info, warnings) = ProjectFileReader.ReadSafe(Fixtures.Path("proj-malformed/Nope.csproj"));
        Assert.Null(info);
        Assert.Single(warnings);
    }

    [Fact]
    public void DoctypeInCsproj_IsRefusedNotResolved()
    {
        using var t = new TempTree();
        var path = t.Add("Web.csproj");
        File.WriteAllText(path, "<!DOCTYPE Project [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><Project><ItemGroup><PackageReference Include=\"A\" Version=\"&x;\"/></ItemGroup></Project>");
        var (info, warnings) = ProjectFileReader.ReadSafe(path, t.Root);
        Assert.Null(info);
        Assert.Single(warnings);
    }

    [Fact]
    public void OpenRange_WithoutLock_IsUnresolved()
    {
        var r = Pkg(Read("proj-ranges", "Web/Web.csproj"), "Open.Range");
        Assert.Null(r.Version);
        Assert.Equal("[13.0.0,14.0.0)", r.VersionText);
        Assert.Contains("[13.0.0,14.0.0)", r.UnresolvedReason);
    }

    [Fact]
    public void ExactRange_ResolvesToMinimum()
    {
        var r = Pkg(Read("proj-ranges", "Web/Web.csproj"), "Exact.Range");
        Assert.Equal(NuGetVersion.Parse("17.7.2"), r.Version);
        Assert.Null(r.UnresolvedReason);
    }

    [Fact]
    public void BareVersion_IsThatVersion()
    {
        Assert.Equal(NuGetVersion.Parse("6.2.1"), Pkg(Read("proj-ranges", "Web/Web.csproj"), "Bare.Version").Version);
    }

    [Fact]
    public void Cpm_MalformedPackagesProps_WarnsAndRefsUnresolved()
    {
        var (info, warnings) = ProjectFileReader.ReadSafe(Fixtures.Path("proj-cpm-malformed/Web/Web.csproj"), Fixtures.Path("proj-cpm-malformed"));
        Assert.NotNull(info);
        var r = info!.Packages.Single(x => x.Id == "Contentment");
        Assert.Null(r.Version);
        Assert.StartsWith("unresolved MSBuild property", r.UnresolvedReason);
        var w = Assert.Single(warnings);
        Assert.Contains("Directory.Packages.props", w);
    }

    [Fact]
    public void Cpm_DisabledByProperty_UsesCsprojVersions()
    {
        var r = Pkg(Read("proj-cpm-disabled", "Web/Web.csproj"), "Contentment");
        Assert.Equal(NuGetVersion.Parse("6.1.0"), r.Version);
    }

    [Fact]
    public void Cpm_GlobalPackageReference_IsListed()
    {
        var p = Read("proj-cpm-global", "Web/Web.csproj");
        var g = Pkg(p, "Our.Analyzer");
        Assert.Equal(NuGetVersion.Parse("2.0.0"), g.Version);
        Assert.True(g.Direct);
    }

    [Fact]
    public void Cpm_PackageWithNoCentralVersion_IsUnresolvedNotCrash()
    {
        using var t = new TempTree();
        File.WriteAllText(t.Add("Directory.Packages.props"), "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        var cs = t.Add("Web.csproj");
        File.WriteAllText(cs, "<Project><ItemGroup><PackageReference Include=\"Missing.Pkg\"/></ItemGroup></Project>");
        var r = Assert.Single(ProjectFileReader.Read(cs, t.Root).Packages);
        Assert.Null(r.Version);
        Assert.NotNull(r.UnresolvedReason);
    }

    [Fact]
    public void DuplicatePackageReference_InOneCsproj_FirstWinsWithWarning()
    {
        var (info, warnings) = ProjectFileReader.ReadSafe(Fixtures.Path("proj-duplicate/Web/Web.csproj"), Fixtures.Path("proj-duplicate"));
        var r = Assert.Single(info!.Packages);
        Assert.Equal("Contentment", r.Id);
        Assert.Equal(NuGetVersion.Parse("6.2.1"), r.Version);
        var w = Assert.Single(warnings);
        Assert.Contains("Contentment", w);
    }

    [Fact]
    public void Reader_NeverModifiesScannedFiles()
    {
        var path = Fixtures.Path("proj-cpm/Web/Web.csproj");
        var before = File.ReadAllBytes(path);
        var stamp = File.GetLastWriteTimeUtc(path);
        Read("proj-cpm", "Web/Web.csproj");
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    private static string Csproj(TempTree t, string relative, string items)
    {
        var path = t.Add(relative);
        File.WriteAllText(path, $"<Project><ItemGroup>{items}</ItemGroup></Project>");
        return path;
    }

    private const string PropsAbove = "<Project><PropertyGroup><UmbracoVersion>17.7.2</UmbracoVersion></PropertyGroup></Project>";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultWalkUp_StopsAtGitFolderOrFile(bool gitIsFile)
    {
        using var t = new TempTree();
        File.WriteAllText(t.Add("Directory.Build.props"), PropsAbove); // above the repo root: must not be read
        var gitPath = Path.Combine(t.Root, "repo", ".git");
        if (gitIsFile)
        {
            t.Add("repo/.git"); // submodule style: .git is a file
        }
        else
        {
            Directory.CreateDirectory(gitPath);
        }

        var cs = Csproj(t, "repo/src/Web.csproj", "<PackageReference Include=\"A\" Version=\"$(UmbracoVersion)\" />");
        var r = Assert.Single(ProjectFileReader.Read(cs).Packages);
        Assert.Null(r.Version);
        Assert.NotNull(r.UnresolvedReason);
    }

    [Fact]
    public void DefaultWalkUp_ReadsPropsInsideTheRepo()
    {
        using var t = new TempTree();
        Directory.CreateDirectory(Path.Combine(t.Root, "repo", ".git"));
        File.WriteAllText(t.Add("repo/Directory.Build.props"), PropsAbove);
        var cs = Csproj(t, "repo/src/Web.csproj", "<PackageReference Include=\"A\" Version=\"$(UmbracoVersion)\" />");
        Assert.Equal(NuGetVersion.Parse("17.7.2"), Assert.Single(ProjectFileReader.Read(cs).Packages).Version);
    }

    private static PackageRef CpmRef(string packagesBody, string? csprojItem = null)
    {
        using var t = new TempTree();
        File.WriteAllText(t.Add("Directory.Packages.props"),
            $"<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>{packagesBody}</Project>");
        var cs = Csproj(t, "Web.csproj", csprojItem ?? "<PackageReference Include=\"A\" />");
        return Assert.Single(ProjectFileReader.Read(cs, t.Root).Packages);
    }

    [Fact]
    public void Cpm_FloatingVersionInProps_IsUnresolved()
    {
        var r = CpmRef("<ItemGroup><PackageVersion Include=\"A\" Version=\"17.*\" /></ItemGroup>");
        Assert.Null(r.Version);
        Assert.Equal("17.*", r.VersionText);
        Assert.Contains("17.*", r.UnresolvedReason);
    }

    [Fact]
    public void Cpm_OpenRangeInProps_IsUnresolved()
    {
        var r = CpmRef("<ItemGroup><PackageVersion Include=\"A\" Version=\"[13.0.0,14.0.0)\" /></ItemGroup>");
        Assert.Null(r.Version);
        Assert.Contains("[13.0.0,14.0.0)", r.UnresolvedReason);
    }

    [Fact]
    public void Cpm_PropertyDefinedInPackagesProps_IsExpanded()
    {
        var r = CpmRef("<PropertyGroup><AVer>17.7.2</AVer></PropertyGroup><ItemGroup><PackageVersion Include=\"A\" Version=\"$(AVer)\" /></ItemGroup>");
        Assert.Equal(NuGetVersion.Parse("17.7.2"), r.Version);
    }

    [Fact]
    public void Cpm_ConditionedEntriesWithDifferentVersions_AreUnresolved()
    {
        var r = CpmRef(
            "<ItemGroup Condition=\"'$(TargetFramework)'=='net8.0'\"><PackageVersion Include=\"A\" Version=\"1.0.0\" /></ItemGroup>" +
            "<ItemGroup Condition=\"'$(TargetFramework)'=='net10.0'\"><PackageVersion Include=\"A\" Version=\"2.0.0\" /></ItemGroup>");
        Assert.Null(r.Version);
        Assert.Contains("1.0.0", r.VersionText);
        Assert.Contains("2.0.0", r.VersionText);
        Assert.Contains("differ", r.UnresolvedReason);
    }

    [Fact]
    public void ConditionedPackageReferences_WithDifferentVersions_AreUnresolved()
    {
        using var t = new TempTree();
        var cs = Csproj(t, "Web.csproj",
            "<PackageReference Include=\"A\" Version=\"1.0.0\" Condition=\"'$(X)'=='1'\" /><PackageReference Include=\"A\" Version=\"2.0.0\" Condition=\"'$(X)'=='2'\" />");
        var (info, warnings) = ProjectFileReader.ReadSafe(cs, t.Root);
        var r = Assert.Single(info!.Packages);
        Assert.Null(r.Version);
        Assert.Contains("1.0.0", r.VersionText);
        Assert.Contains("2.0.0", r.VersionText);
        Assert.Contains("differ", r.UnresolvedReason);
        Assert.Single(warnings);
    }

    [Fact]
    public void ConditionedPackageReferences_WithSameVersion_Resolve()
    {
        using var t = new TempTree();
        var cs = Csproj(t, "Web.csproj",
            "<PackageReference Include=\"A\" Version=\"1.0.0\" Condition=\"'$(X)'=='1'\" /><PackageReference Include=\"A\" Version=\"1.0.0\" Condition=\"'$(X)'=='2'\" />");
        Assert.Equal(NuGetVersion.Parse("1.0.0"), Assert.Single(ProjectFileReader.Read(cs, t.Root).Packages).Version);
    }

    [Fact]
    public void UmbracoVersion_IsHighestOfCmsAndCore_WhenCmsIsHigher()
    {
        using var t = new TempTree();
        var cs = Csproj(t, "Web.csproj",
            "<PackageReference Include=\"Umbraco.Cms\" Version=\"17.7.2\" /><PackageReference Include=\"Umbraco.Cms.Core\" Version=\"17.1.0\" />");
        Assert.Equal(NuGetVersion.Parse("17.7.2"), ProjectFileReader.Read(cs, t.Root).UmbracoVersion);
    }
}
