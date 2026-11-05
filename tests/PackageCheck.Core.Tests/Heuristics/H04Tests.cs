using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Heuristics;

public class H04Tests
{
    private static PackageFacts Facts(params UmbracoDependency[] umbraco) =>
        new("Pkg", NuGetVersion.Parse("1.0.0"), null!, null, umbraco, [], [], null, [], null, [], []);

    [Fact]
    public void H04_NoPath_EmitsNotUmbracoInfo()
    {
        var ev = new H04UmbracoPath().Evaluate(Facts(), Target("17.7.2")).Single();
        Assert.Equal(("H04", EvidenceResult.Info, EvidenceWeight.Info), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("no path to Umbraco.Cms.* within depth 3", ev.Detail);
    }

    [Fact]
    public void H04_DirectDependency_EmitsNothing() =>
        Assert.Empty(new H04UmbracoPath().Evaluate(Facts(new UmbracoDependency("Pkg", "Umbraco.Cms.Core", VersionRange.Parse("[17.0.0, )"), 1)), Target("17.7.2")));

    [Fact]
    public void H04_TransitivePath_NamesTheVia()
    {
        var ev = new H04UmbracoPath().Evaluate(
            Facts(new UmbracoDependency("jcdcdev.Umbraco.Core", "Umbraco.Cms.Core", VersionRange.Parse("[17.0.0, 18.0.0)"), 2)),
            Target("17.7.2")).Single();
        Assert.Equal(EvidenceResult.Info, ev.Result);
        Assert.Equal("Umbraco.Cms.Core reached via jcdcdev.Umbraco.Core at depth 2", ev.Detail);
    }

    [Fact]
    public void H04_AppliesToEveryTarget() => Assert.True(new H04UmbracoPath().AppliesTo(Target("13.5.0", "net8.0")));
}
