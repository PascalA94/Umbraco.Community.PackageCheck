namespace PackageCheck.Inputs.Tests;

public class CentralPackageVersionsTests
{
    private static CentralPackageVersions? For(string fixture, string csproj)
    {
        var path = Fixtures.Path($"{fixture}/{csproj}");
        var root = Fixtures.Path(fixture);
        return CentralPackageVersions.ForProject(path, MsBuildProperties.ForProject(path, root), root);
    }

    [Fact]
    public void VersionFor_IsCaseInsensitive()
    {
        var c = For("proj-cpm", "Web/Web.csproj");
        Assert.Equal("6.2.1", c!.VersionFor("contentment"));
        Assert.Null(c.VersionFor("Nope"));
    }

    [Fact]
    public void NoPackagesProps_ReturnsNull()
    {
        Assert.Null(For("proj-floating", "Web/Web.csproj"));
    }

    [Fact]
    public void DisabledByProperty_ReturnsNull()
    {
        Assert.Null(For("proj-cpm-disabled", "Web/Web.csproj"));
    }

    [Fact]
    public void ImportOfParentProps_IsFollowed_AndNearerEntryWins()
    {
        var c = For("proj-cpm-import", "sub/Web/Web.csproj");
        Assert.Equal("1.0.0", c!.VersionFor("Parent.Pkg"));
        Assert.Equal("2.0.0", c.VersionFor("Local.Pkg"));
    }

    [Fact]
    public void MalformedProps_IsFlagged_AndHasNoVersions()
    {
        var c = For("proj-cpm-malformed", "Web/Web.csproj");
        Assert.NotNull(c);
        Assert.NotNull(c!.Warning);
        Assert.Contains("Directory.Packages.props", c.Warning);
        Assert.Null(c.VersionFor("Contentment"));
    }

    [Fact]
    public void GlobalPackageReferences_AreExposed()
    {
        var c = For("proj-cpm-global", "Web/Web.csproj");
        var g = Assert.Single(c!.GlobalReferences);
        Assert.Equal(("Our.Analyzer", "2.0.0"), (g.Id, g.VersionText));
    }

    [Fact]
    public void ImportCycle_DoesNotHang()
    {
        using var t = new TempTree();
        var props = t.Add("Directory.Packages.props");
        File.WriteAllText(props, "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><Import Project=\"Directory.Packages.props\"/><ItemGroup><PackageVersion Include=\"A\" Version=\"1.0.0\"/></ItemGroup></Project>");
        var cs = t.Add("Web.csproj");
        var c = CentralPackageVersions.ForProject(cs, MsBuildProperties.ForProject(cs, t.Root), t.Root);
        Assert.Equal("1.0.0", c!.VersionFor("A"));
    }
}

public class MsBuildPropertiesTests
{
    [Fact]
    public void Expand_UnresolvedProperty_ReturnsNull()
    {
        var path = Fixtures.Path("unresolved-prop/Web/Web.csproj");
        var p = MsBuildProperties.ForProject(path, Fixtures.Path("unresolved-prop"));
        Assert.Null(p.Expand("$(UmbracoVersion)"));
        Assert.Equal("1.2", p.Expand("1.2"));
    }

    [Fact]
    public void Expand_SelfReferencingProperty_DoesNotLoopForever()
    {
        using var t = new TempTree();
        File.WriteAllText(t.Add("Directory.Build.props"), "<Project><PropertyGroup><A>$(B)</A><B>$(A)</B></PropertyGroup></Project>");
        var cs = t.Add("Web.csproj");
        Assert.Null(MsBuildProperties.ForProject(cs, t.Root).Expand("$(A)"));
    }

    [Fact]
    public void Expand_ExponentialFanOut_IsUnresolvedQuickly()
    {
        using var t = new TempTree();
        // Each level doubles the previous one: 2^40 substitutions without a work budget.
        var body = "<B0>x</B0>" + string.Concat(Enumerable.Range(1, 40).Select(i => $"<B{i}>$(B{i - 1})$(B{i - 1})</B{i}>"));
        // Self-referencing fan-out: ten references per level, bounded only by depth without a budget.
        body += "<S>" + string.Concat(Enumerable.Repeat("$(S)", 10)) + "x</S>";
        File.WriteAllText(t.Add("Directory.Build.props"), $"<Project><PropertyGroup>{body}</PropertyGroup></Project>");
        var props = MsBuildProperties.ForProject(t.Add("Web.csproj"), t.Root);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(props.Expand("$(B40)"));
        Assert.Null(props.Expand("$(S)"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"took {sw.Elapsed}");
        Assert.Equal("x", props.Expand("$(B0)"));
    }
}
