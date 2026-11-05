namespace PackageCheck.Inputs.Tests;

public class SolutionFileReaderTests
{
    private static string Fixture(string relative) => Fixtures.Path(relative);

    private static string Rel(string root, string path) =>
        Path.GetRelativePath(Fixture(root), path).Replace('\\', '/');

    [Fact]
    public void Slnx_ListsProjectsInNestedFolders()
    {
        var r = SolutionFileReader.Read(Fixture("slnx-nested/Site.slnx"));
        Assert.Empty(r.Warnings);
        Assert.Equal(
            new[]
            {
                "src/Root/Root.csproj", "src/Shared/Shared.csproj", "src/web/Site.Core/Site.Core.csproj",
                "src/web/sites/Site.A/Site.A.csproj", "tests/Shared.Tests/Shared.Tests.csproj",
            },
            r.ProjectPaths.Select(p => Rel("slnx-nested", p)).Order(StringComparer.Ordinal));
        Assert.All(r.ProjectPaths, p => Assert.True(Path.IsPathRooted(p)));
    }

    [Fact]
    public void Sln_SkipsSolutionFolders()
    {
        var r = SolutionFileReader.Read(Fixture("sln-folders/Site.sln"));
        Assert.Equal(2 + 1, r.ProjectPaths.Count); // Site.A, Shared and the .fsproj
        Assert.DoesNotContain(r.ProjectPaths, p => Path.GetFileName(p) is "src" or "web");
    }

    [Fact]
    public void Sln_DropsUrlWebSitesAndNonCsprojFamilyTypes_Silently()
    {
        var r = SolutionFileReader.Read(Fixture("sln-folders/Site.sln"));
        Assert.Equal(
            new[] { "src/Fs/Fs.fsproj", "src/Shared/Shared.csproj", "src/web/Site.A/Site.A.csproj" },
            r.ProjectPaths.Select(p => Rel("sln-folders", p)).Order(StringComparer.Ordinal));
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void MissingProjectInSolution_IsSkippedWithWarning()
    {
        var r = SolutionFileReader.Read(Fixture("sln-missing-project/Site.sln"));
        Assert.Single(r.ProjectPaths);
        Assert.Contains(r.Warnings, w => w.Contains("Gone.csproj") && w.Contains("not found"));
    }

    [Fact]
    public void MalformedSlnx_WarnsAndReturnsNoProjects()
    {
        var file = Fixture("slnx-truncated/Broken.slnx");
        var r = SolutionFileReader.Read(file);
        Assert.Empty(r.ProjectPaths);
        var w = Assert.Single(r.Warnings);
        Assert.Contains("Broken.slnx", w);
    }

    [Fact]
    public void MalformedSln_WarnsAndReturnsNoProjects()
    {
        var r = SolutionFileReader.Read(Fixture("sln-garbage/Broken.sln"));
        Assert.Empty(r.ProjectPaths);
        var w = Assert.Single(r.Warnings);
        Assert.Contains("Broken.sln", w);
    }

    [Fact]
    public void UnreadableSolutionPath_WarnsAndNeverThrows()
    {
        var r = SolutionFileReader.Read(Fixture("does-not-exist/Site.sln"));
        Assert.Empty(r.ProjectPaths);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void Solution_SameProjectListedTwice_Once()
    {
        var r = SolutionFileReader.Read(Fixture("sln-duplicate/Site.sln"));
        Assert.Single(r.ProjectPaths);
        Assert.Contains(r.Warnings, w => w.Contains("Site.sln"));
    }

    [Fact]
    public void Slnx_SameProjectListedTwiceInOneFolder_Once()
    {
        // The library throws on this; the XML fallback keeps the project and the run warns.
        var r = SolutionFileReader.Read(Fixture("slnx-duplicate-one-folder/Site.slnx"));
        Assert.Single(r.ProjectPaths);
        Assert.Contains(r.Warnings, w => w.Contains("Site.slnx"));
    }

    [Fact]
    public void Solution_SameProjectTwoSpellingsInDifferentFolders_Once()
    {
        var r = SolutionFileReader.Read(Fixture("slnx-duplicate-and-parent/src/Site.slnx"));
        Assert.Equal(
            new[] { "Shared/Shared.csproj", "src/Web/Web.csproj" },
            r.ProjectPaths.Select(p => Rel("slnx-duplicate-and-parent", p)).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(r.ProjectPaths, p => p.Contains(".."));
    }

    [Fact]
    public void Sln_Fallback_SkipsSolutionFoldersAndFiltersTypes()
    {
        var r = SolutionFileReader.Read(Fixture("sln-duplicate-with-folders/Site.sln"));
        Assert.Equal(new[] { "A/A.csproj" }, r.ProjectPaths.Select(p => Rel("sln-duplicate-with-folders", p)));
    }

    [Fact]
    public void Reconcile_EqualCountsDifferentMembers_WarnsAndReturnsUnion()
    {
        var warnings = new List<string>();
        var merged = SolutionFileReader.ReconcileSlnx(["a/A.csproj", "b/B.csproj"], ["a/A.csproj", "c/C.csproj"], "S.slnx", warnings);
        Assert.Equal(new[] { "a/A.csproj", "b/B.csproj", "c/C.csproj" }, merged.Distinct().Order(StringComparer.Ordinal));
        Assert.Single(warnings);
    }

    // Spike S4: the library silently drops projects under Folder elements nested in Folder
    // elements; the cross-check must turn that into a visible warning, not a short list.
    [Fact]
    public void Slnx_LibraryAndXmlCountsDisagree_WarnsAndKeepsEveryProject()
    {
        var r = SolutionFileReader.Read(Fixture("slnx-nested-folders-malformed/Site.slnx"));
        Assert.Contains(r.Warnings, w => w.Contains("Site.slnx") && w.Contains("2") && w.Contains("4"));
        Assert.Equal(
            new[] { "src/Shared/Shared.csproj", "src/web/Site.Core/Site.Core.csproj", "src/web/sites/Site.A/Site.A.csproj", "tests/Shared.Tests/Shared.Tests.csproj" },
            r.ProjectPaths.Select(p => Rel("slnx-nested-folders-malformed", p)).Order(StringComparer.Ordinal));
    }
}
