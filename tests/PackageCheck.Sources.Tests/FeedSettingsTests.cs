using PackageCheck.Sources.NuGet;

namespace PackageCheck.Sources.Tests;

public class FeedSettingsTests
{
    // Security review M3 (S1): tokens also live in paths (Cloudsmith entitlements, MyGet /auth/<key>/), so only
    // scheme, host and port are ever shown. A '/' inside a password must not move the host.
    [Theory]
    [InlineData("https://user:pass@feed.test/v3/index.json", "https://feed.test")]
    [InlineData("https://feed.test/v3/index.json?code=SECRET&x=1", "https://feed.test")]
    [InlineData("https://user@feed.test:8443/path#frag", "https://feed.test:8443")]
    [InlineData("https://api.nuget.org/v3/index.json", "https://api.nuget.org")]
    [InlineData("https://dl.cloudsmith.io/SECRETTOKEN/org/repo/nuget/index.json", "https://dl.cloudsmith.io")]
    [InlineData("https://www.myget.org/F/feed/auth/SECRET-KEY/api/v3/index.json", "https://www.myget.org")]
    [InlineData("https://user:pa/ss@host.test/v3/index.json", "https://host.test")]
    [InlineData("not a url://user:pw@x", "not a url://x")]
    [InlineData("/local/feed", "/local/feed")]
    public void Redact_StripsUserInfoQueryAndFragment(string input, string expected) =>
        Assert.Equal(expected, FeedSettings.Redact(input));

    [Fact]
    public void SourcesFor_WithoutMapping_IsEveryEnabledSource()
    {
        using var f = new TestFeeds();
        var site = f.WriteConfig("site", [("a", "https://a.test/v3/index.json"), ("b", "https://b.test/v3/index.json")]);

        Assert.Equal(["a", "b"], f.Settings(site).SourcesFor("Any.Id").Select(s => s.Name));
    }

    [Fact]
    public void SourcesFor_WithMapping_IsOnlyMappedSources_CaseInsensitive()
    {
        using var f = new TestFeeds();
        var site = f.WriteConfig("site", [("a", "https://a.test/v3/index.json"), ("b", "https://b.test/v3/index.json")], """
              <packageSourceMapping>
                <packageSource key="a"><package pattern="Umbraco.*" /></packageSource>
                <packageSource key="b"><package pattern="*" /></packageSource>
              </packageSourceMapping>
            """);
        var settings = f.Settings(site);

        Assert.Equal(["a"], settings.SourcesFor("umbraco.community.contentment").Select(s => s.Name));
        Assert.Equal(["b"], settings.SourcesFor("Other.Thing").Select(s => s.Name));
    }

    [Fact]
    public void DisabledSource_IsExcluded()
    {
        using var f = new TestFeeds();
        var site = f.WriteConfig("site", [("a", "https://a.test/v3/index.json"), ("b", "https://b.test/v3/index.json")],
            """<disabledPackageSources><add key="b" value="true" /></disabledPackageSources>""");

        Assert.Equal(["a"], f.Settings(site).Sources.Select(s => s.Name));
    }

    [Fact]
    public void Folders_AreTheOnesGiven_SoTheMachineHttpCacheNeverHidesRequests()
    {
        using var f = new TestFeeds();
        var settings = f.Settings(f.WriteConfig("site", []));

        Assert.Equal(f.GlobalPackages, settings.GlobalPackagesFolder);
        Assert.Equal(f.HttpCache, settings.HttpCacheFolder);
        Assert.Equal(f.Temp, settings.TempFolder);
    }

    [Fact]
    public void DefaultHttpCache_IsTheToolsOwn()
    {
        using var f = new TestFeeds();
        var settings = FeedSettings.ForSite(f.WriteConfig("site", []));

        Assert.Contains("umbraco-package-check", settings.HttpCacheFolder);
    }
}
