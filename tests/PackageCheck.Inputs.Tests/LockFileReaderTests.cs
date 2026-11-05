using NuGet.Versioning;
using PackageCheck.Core.Model;

namespace PackageCheck.Inputs.Tests;

public class LockFileReaderTests
{
    private static string Lock(string fixture) => Fixtures.Path($"{fixture}/Web/packages.lock.json");

    [Fact]
    public void LockV2_TransitiveUmbracoPackage_IsDirectFalse()
    {
        var (refs, warnings) = LockFileReader.Read(Lock("lock-v2"), "net10.0");
        Assert.Empty(warnings);
        var wf = refs.Single(r => r.Id == "Umbraco.Workflow.Core");
        Assert.False(wf.Direct);
        Assert.Equal(NuGetVersion.Parse("17.1.0"), wf.Version);
        Assert.True(refs.Single(r => r.Id == "Umbraco.Cms").Direct);
        Assert.False(refs.Single(r => r.Id == "Newtonsoft.Json").Direct);
        Assert.DoesNotContain(refs, r => r.Id == "Web.Shared");
    }

    [Fact]
    public void LockFile_ResolvesFloatingVersion()
    {
        var project = ProjectFileReader.Read(Fixtures.Path("lock-floating/Web/Web.csproj"), Fixtures.Path("lock-floating"));
        Assert.Null(project.UmbracoVersion);
        Assert.NotNull(project.Packages.Single().UnresolvedReason);

        var (refs, _) = LockFileReader.Read(Lock("lock-floating"), "net10.0");
        var (applied, warnings) = LockFileReader.Apply(project, refs);
        Assert.Empty(warnings);

        var cms = applied.Packages.Single(p => p.Id == "Umbraco.Cms");
        Assert.Equal(NuGetVersion.Parse("17.7.2"), cms.Version);
        Assert.Null(cms.UnresolvedReason);
        Assert.Equal("17.*", cms.VersionText);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), applied.UmbracoVersion);
    }

    [Fact]
    public void Apply_LockedVersionWins_OverResolvedVersion_AndIgnoresIdCase()
    {
        var project = new ProjectInfo("p.csproj", ["net10.0"], NuGetVersion.Parse("17.0.0"),
            [new PackageRef("umbraco.cms", "17.0.0", NuGetVersion.Parse("17.0.0"), true, null)]);
        var (applied, _) = LockFileReader.Apply(project, [new PackageRef("Umbraco.Cms", null, NuGetVersion.Parse("17.7.2"), true, null)]);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), applied.Packages.Single().Version);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), applied.UmbracoVersion);
    }

    [Fact]
    public void Apply_TransitiveLockEntry_DoesNotOverlayDirectRef()
    {
        var project = new ProjectInfo("p.csproj", ["net10.0"], null,
            [new PackageRef("Our.Thing", "1.*", null, true, "unresolved MSBuild property: 1.*")]);
        var (applied, _) = LockFileReader.Apply(project, [new PackageRef("Our.Thing", null, NuGetVersion.Parse("1.4.0"), false, null)]);
        Assert.Null(applied.Packages.Single().Version);
    }

    [Fact]
    public void LockV1_IsRejectedWithWarning()
    {
        var (refs, warnings) = LockFileReader.Read(Lock("lock-v1"), "net10.0");
        Assert.Empty(refs);
        var w = Assert.Single(warnings);
        Assert.Contains("packages.lock.json", w);
        Assert.Contains("version", w);
    }

    [Fact]
    public void LockFile_PicksTfmMatchingProject()
    {
        var (net8, _) = LockFileReader.Read(Lock("lock-v2"), "net8.0");
        Assert.Equal(NuGetVersion.Parse("13.2.0"), net8.Single(r => r.Id == "Umbraco.Workflow.Core").Version);
        Assert.DoesNotContain(net8, r => r.Id == "Umbraco.Cms");
        Assert.DoesNotContain(net8, r => r.Id == "Only.Rid");

        var (net10, _) = LockFileReader.Read(Lock("lock-v2"), "NET10.0");
        Assert.Equal(NuGetVersion.Parse("17.1.0"), net10.Single(r => r.Id == "Umbraco.Workflow.Core").Version);
    }

    [Fact]
    public void LockFile_NoMatchingTfm_WarnsAndReturnsEmpty()
    {
        var (refs, warnings) = LockFileReader.Read(Lock("lock-v2"), "net9.0");
        Assert.Empty(refs);
        Assert.Contains("net9.0", Assert.Single(warnings));
    }

    [Fact]
    public void LockFile_Malformed_WarnsAndReturnsEmpty()
    {
        var (refs, warnings) = LockFileReader.Read(Lock("lock-malformed"), "net10.0");
        Assert.Empty(refs);
        Assert.Contains("packages.lock.json", Assert.Single(warnings));
    }

    [Fact]
    public void LockFile_Missing_WarnsAndReturnsEmpty()
    {
        var (refs, warnings) = LockFileReader.Read(Fixtures.Path("lock-v2/nope/packages.lock.json"), "net10.0");
        Assert.Empty(refs);
        Assert.Single(warnings);
    }

    [Fact]
    public void LockFile_DeeplyNested_WarnsInsteadOfCrashing()
    {
        using var t = new TempTree();
        var path = t.Add("packages.lock.json");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("[", 5000)) + string.Concat(Enumerable.Repeat("]", 5000)));
        var (refs, warnings) = LockFileReader.Read(path, "net10.0");
        Assert.Empty(refs);
        Assert.Single(warnings);
    }

    [Fact]
    public void LockFile_WrongShapedEntries_AreIgnoredWithoutThrowing()
    {
        using var t = new TempTree();
        var path = t.Add("packages.lock.json");
        File.WriteAllText(path, """{"version":2,"dependencies":{"net10.0":{"A":"x","B":{"type":"Direct","resolved":"not-a-version"},"C":{"type":"Direct","resolved":5}}}}""");
        var (refs, _) = LockFileReader.Read(path, "net10.0");
        Assert.Empty(refs);
    }

    [Fact]
    public void Apply_StaleLock_LeavesDirectRefUnchangedAndWarns()
    {
        var floating = new PackageRef("Our.Thing", "1.*", null, true, "unresolved MSBuild property: 1.* (floating version)");
        var project = new ProjectInfo("/x/Web.csproj", ["net10.0"], null, [floating]);
        var (applied, warnings) = LockFileReader.Apply(project, []);
        Assert.Equal(floating, applied.Packages.Single());
        Assert.Equal("Web.csproj: lock file has no entry for Our.Thing", Assert.Single(warnings));
    }
}
