using PackageCheck.Core;

namespace PackageCheck.Inputs.Tests;

public class FolderDiscoveryTests
{
    [Fact]
    public void Folder_PrefersSlnxThenSlnThenCsproj()
    {
        using var t = new TempTree();
        t.Add("Web/Web.csproj");
        var sln = t.Add("a/b/b.sln");
        var slnx = t.Add("a/b/a.slnx");

        var e = FolderDiscovery.FindEntry(t.Root);
        Assert.Equal(EntryKind.Slnx, e.Kind);
        Assert.Equal(slnx, e.Path);

        File.Delete(slnx);
        e = FolderDiscovery.FindEntry(t.Root);
        Assert.Equal(EntryKind.Sln, e.Kind);
        Assert.Equal(sln, e.Path);
    }

    [Fact]
    public void Folder_WithOnlyCsprojFiles_IsAProjectSet()
    {
        using var t = new TempTree();
        t.Add("A/A.csproj");
        t.Add("B/B.csproj");
        var e = FolderDiscovery.FindEntry(t.Root);
        Assert.Equal(EntryKind.ProjectSet, e.Kind);
        Assert.Equal(t.Root, e.Path);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData(".git")]
    public void Folder_SkipsBinObjNodeModulesGit(string skipped)
    {
        using var t = new TempTree();
        t.Add($"{skipped}/Inner/Inner.csproj");
        var ex = Assert.Throws<NoUmbracoProjectException>(() => FolderDiscovery.FindEntry(t.Root));
        Assert.Equal($"no Umbraco project found in {t.Root}", ex.Message);
    }

    [Fact]
    public void Folder_SearchesAtMostFourLevels()
    {
        using var t = new TempTree();
        t.Add("a/b/c/d/e/Deep.sln"); // five folders down
        Assert.Throws<NoUmbracoProjectException>(() => FolderDiscovery.FindEntry(t.Root));

        var four = t.Add("a/b/c/d/Ok.sln"); // four folders down
        var e = FolderDiscovery.FindEntry(t.Root);
        Assert.Equal(four, e.Path);
    }

    [Fact]
    public void MissingPath_ThrowsPathNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), "pc-does-not-exist-" + Guid.NewGuid());
        var ex = Assert.Throws<PathNotFoundException>(() => FolderDiscovery.FindEntry(path));
        Assert.Equal($"path not found: {path}", ex.Message);
        Assert.IsAssignableFrom<UsageException>(ex);
    }

    [Theory]
    [InlineData("Site.slnx", EntryKind.Slnx)]
    [InlineData("Site.sln", EntryKind.Sln)]
    [InlineData("Site.csproj", EntryKind.Csproj)]
    public void ExistingFile_IsItsOwnEntry(string name, EntryKind kind)
    {
        using var t = new TempTree();
        var file = t.Add(name);
        var e = FolderDiscovery.FindEntry(file);
        Assert.Equal(kind, e.Kind);
        Assert.Equal(file, e.Path);
    }

    [Fact]
    public void ExistingFile_OfAnotherType_ThrowsNoUmbracoProject()
    {
        using var t = new TempTree();
        Assert.Throws<NoUmbracoProjectException>(() => FolderDiscovery.FindEntry(t.Add("notes.txt")));
    }
}
