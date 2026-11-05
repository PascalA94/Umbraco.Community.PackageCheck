using NuGet.Versioning;
using PackageCheck.Core;
using PackageCheck.Core.Model;
using PackageCheck.Inputs;

namespace PackageCheck.Inputs.Tests;

public class ProjectReaderTests
{
    private static Task<ScanInput> Read(string fixture, bool transitive = false) =>
        new ProjectReader().ReadAsync(Fixtures.Path(fixture), new InputOptions(transitive, false, Fixtures.Path(fixture)), CancellationToken.None);

    private static ProjectInfo Proj(string name, params PackageRef[] refs) =>
        new($"/s/{name}/{name}.csproj", ["net10.0"], PackageRefs.UmbracoVersionOf(refs), refs);

    private static PackageRef Ref(string id, string? version, bool direct = true) =>
        new(id, version, version is null ? null : NuGetVersion.Parse(version), direct, null);

    private static ScanInput Input(params ProjectInfo[] projects) => new("/s", projects, [], TransitiveStatus.NotRequested);

    [Fact]
    public async Task NoUmbracoProject_ThrowsNoUmbracoProjectException()
    {
        var ex = await Assert.ThrowsAsync<NoUmbracoProjectException>(() => Read("shape-10"));
        Assert.StartsWith("no Umbraco project found", ex.Message);
    }

    [Fact]
    public async Task MissingPath_ThrowsPathNotFoundException() =>
        await Assert.ThrowsAsync<PathNotFoundException>(() => Read("does-not-exist"));

    [Fact]
    public async Task TestProjectOutsideSrc_IsIncluded()
    {
        var input = await Read("shape-05");
        Assert.Contains(input.Projects, p => p.Path.EndsWith("Web.Tests.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MixedMajors_AddsSiteWarning_AndUsesHighest()
    {
        var input = await Read("shape-09");
        Assert.Contains(input.Warnings, w => w.Contains("mixed Umbraco majors"));
        Assert.Equal(NuGetVersion.Parse("17.7.2"), input.SiteUmbracoVersion);
    }

    [Fact]
    public async Task Transitive_NoLockFile_IsUnavailable()
    {
        var input = await Read("shape-03", transitive: true);
        Assert.Equal(TransitiveStatus.Unavailable, input.Transitive);
        Assert.Contains(input.Warnings, w => w.Contains("packages.lock.json"));
    }

    [Fact]
    public async Task Transitive_WithLockFile_AppendsNonDirectRefs()
    {
        var input = await Read("shape-04", transitive: true);
        var workflow = Assert.Single(input.Projects[0].Packages, p => p.Id == "Umbraco.Workflow");
        Assert.False(workflow.Direct);
        Assert.Equal(TransitiveStatus.FromLockFile, input.Transitive);
    }

    [Fact]
    public async Task LockFile_WithoutTransitiveFlag_DoesNotAppendNonDirectRefs()
    {
        var input = await Read("shape-04");
        Assert.DoesNotContain(input.Projects[0].Packages, p => p.Id == "Umbraco.Workflow");
        Assert.Equal(TransitiveStatus.NotRequested, input.Transitive);
    }

    [Fact]
    public async Task ReaderWarnings_ReachSiteWarnings_AndRunContinues()
    {
        using var t = new TempTree();
        File.WriteAllText(t.Add("Web/Web.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
            + "<ItemGroup><PackageReference Include=\"Umbraco.Cms\" Version=\"17.7.2\" /></ItemGroup></Project>");
        File.WriteAllText(t.Add("Broken/Broken.csproj"), "<Project><not closed");
        File.WriteAllText(t.Add("Web/packages.lock.json"), "{ nope");

        var input = await new ProjectReader().ReadAsync(t.Root, new InputOptions(false, false, t.Root), CancellationToken.None);

        Assert.Single(input.Projects);
        Assert.Contains(input.Warnings, w => w.Contains("Broken.csproj"));
        Assert.Contains(input.Warnings, w => w.Contains("packages.lock.json"));
    }

    [Fact]
    public void SamePackage_TwoVersions_TwoRows()
    {
        var input = Input(
            Proj("Web", Ref("Umbraco.Cms", "17.7.2"), Ref("Contentment", "6.2.1")),
            Proj("Tests", Ref("Contentment", "4.7.0")));
        var rows = SitePackages.From(input).Where(r => r.Ref.Id == "Contentment").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Ref.Version!.ToNormalizedString() == "6.2.1" && r.Projects.SequenceEqual(["Web"]));
        Assert.Contains(rows, r => r.Ref.Version!.ToNormalizedString() == "4.7.0" && r.Projects.SequenceEqual(["Tests"]));
    }

    [Fact]
    public void SamePackage_SameVersion_TwoProjects_OneRow()
    {
        var input = Input(
            Proj("Web", Ref("Umbraco.Cms", "17.7.2"), Ref("Contentment", "6.2.1")),
            Proj("Core", Ref("Contentment", "6.2.1")));
        var row = Assert.Single(SitePackages.From(input), r => r.Ref.Id == "Contentment");
        Assert.Equal(["Core", "Web"], row.Projects);
    }

    [Fact]
    public void SamePackage_IdCaseDiffers_OneRow()
    {
        var input = Input(
            Proj("Web", Ref("Umbraco.Cms", "17.7.2"), Ref("contentment", "6.2.1")),
            Proj("Core", Ref("Contentment", "6.2.1")));
        var row = Assert.Single(SitePackages.From(input), r => r.Ref.Id.Equals("Contentment", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("contentment", row.Ref.Id);
        Assert.Equal(2, row.Projects.Count);
    }

    [Fact]
    public void SamePackage_SameRawVersionText_Unresolved_OneRow()
    {
        var raw = new PackageRef("Our.Thing", "$(V)", null, true, "unresolved MSBuild property $(V)");
        var input = Input(Proj("Web", Ref("Umbraco.Cms", "17.7.2"), raw), Proj("Core", raw));
        var row = Assert.Single(SitePackages.From(input), r => r.Ref.Id == "Our.Thing");
        Assert.Equal(2, row.Projects.Count);
    }

    [Fact]
    public void SiteUmbracoVersion_NullWhenNoUmbracoRef() =>
        Assert.Null(Input(Proj("Lib", Ref("Newtonsoft.Json", "13.0.3"))).SiteUmbracoVersion);
}
