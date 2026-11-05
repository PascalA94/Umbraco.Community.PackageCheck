using Microsoft.Extensions.Time.Testing;
using NuGet.Frameworks;
using NuGet.Versioning;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Tests.Targets;

public class TargetResolverTests
{
    // Test data only: the table the resolver reads, not anything the tool hard-codes.
    private static readonly UmbracoVersionsTable Table = new(
    [
        new UmbracoMajor(13, true, new DateOnly(2026, 12, 14), "net8.0"),
        new UmbracoMajor(16, false, new DateOnly(2026, 6, 12), "net9.0"),
        new UmbracoMajor(17, true, new DateOnly(2028, 11, 27), "net10.0"),
        new UmbracoMajor(18, false, new DateOnly(2027, 6, 25), "net10.0"),
    ]);

    private static readonly CancellationToken Ct = CancellationToken.None;

    private static TargetResolver Resolver(FakeMetadataSource source, DateTimeOffset? now = null) =>
        new(source, new FixedVersionsSource(Table), new FakeTimeProvider(now ?? new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)));

    private static FakeMetadataSource Feed() => new FakeMetadataSource()
        .Add("16.4.0", "net9.0").Add("17.0.0").Add("17.7.1").Add("17.7.2").Add("18.0.0-rc1");

    [Fact]
    public async Task Major_ResolvesLatestStableInMajor()
    {
        var t = await Resolver(Feed()).ResolveAsync("17", null, allowPrerelease: false, Ct);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), t.Resolved);
        Assert.False(t.Prerelease);
        Assert.Equal("17", t.Requested);
    }

    [Fact]
    public async Task Major_SkipsUnlistedVersions() // M2 review I5
    {
        var feed = Feed().Add("17.7.3", listed: false);
        var t = await Resolver(feed).ResolveAsync("17", null, false, Ct);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), t.Resolved);
    }

    [Fact]
    public async Task Exact_ResolvesExactly()
    {
        var t = await Resolver(Feed()).ResolveAsync("17.7.1", null, false, Ct);
        Assert.Equal(NuGetVersion.Parse("17.7.1"), t.Resolved);
    }

    [Fact]
    public async Task Exact_NotOnFeed_Throws()
    {
        var e = await Assert.ThrowsAsync<UsageException>(() => Resolver(Feed()).ResolveAsync("17.99.0", null, false, Ct));
        Assert.Contains("17.99.0", e.Message);
    }

    [Fact]
    public async Task Current_UsesSiteVersion()
    {
        var t = await Resolver(Feed()).ResolveAsync("current", NuGetVersion.Parse("17.7.1"), false, Ct);
        Assert.Equal(NuGetVersion.Parse("17.7.1"), t.Resolved);
        Assert.Equal("current", t.Requested);
    }

    [Fact]
    public async Task Current_WithoutSiteVersion_Throws() =>
        await Assert.ThrowsAsync<UsageException>(() => Resolver(Feed()).ResolveAsync("current", null, false, Ct));

    [Fact]
    public async Task NextMajor_IsSiteMajorPlusOne()
    {
        var feed = Feed().Add("18.0.0").Add("18.3.1");
        var t = await Resolver(feed).ResolveAsync("next-major", NuGetVersion.Parse("17.7.2"), false, Ct);
        Assert.Equal(NuGetVersion.Parse("18.3.1"), t.Resolved);
    }

    [Fact]
    public async Task NextMajor_NoneReleased_Throws()
    {
        var feed = Feed().Add("18.3.1");
        var e = await Assert.ThrowsAsync<UsageException>(() =>
            Resolver(feed).ResolveAsync("next-major", NuGetVersion.Parse("18.3.1"), false, Ct));
        Assert.Contains("no 19.x release", e.Message);
    }

    [Fact]
    public async Task Major_OnlyPrerelease_WithoutFlag_Throws()
    {
        var e = await Assert.ThrowsAsync<UsageException>(() => Resolver(Feed()).ResolveAsync("18", null, false, Ct));
        Assert.Equal("no stable 18.x yet; pass --prerelease or an exact version", e.Message);
    }

    [Fact]
    public async Task Major_OnlyPrerelease_WithFlag_ResolvesAndMarks()
    {
        var t = await Resolver(Feed()).ResolveAsync("18", null, allowPrerelease: true, Ct);
        Assert.Equal(NuGetVersion.Parse("18.0.0-rc1"), t.Resolved);
        Assert.True(t.Prerelease);
    }

    [Fact]
    public async Task ExplicitPrerelease_AlwaysAllowed()
    {
        var t = await Resolver(Feed()).ResolveAsync("18.0.0-rc1", null, allowPrerelease: false, Ct);
        Assert.Equal(NuGetVersion.Parse("18.0.0-rc1"), t.Resolved);
        Assert.True(t.Prerelease);
    }

    [Fact]
    public async Task PrereleaseTarget_RangeVersionIsReleaseBase()
    {
        var pre = await Resolver(Feed()).ResolveAsync("18.0.0-rc1", null, false, Ct);
        Assert.Equal(NuGetVersion.Parse("18.0.0"), pre.RangeVersion);
        Assert.True(VersionRange.Parse("[18.0.0, )").Satisfies(pre.RangeVersion));
        Assert.False(VersionRange.Parse("[17.0.0, 18.0.0)").Satisfies(pre.RangeVersion));

        var stable = await Resolver(Feed()).ResolveAsync("17.7.2", null, false, Ct);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), stable.RangeVersion);
    }

    [Fact]
    public async Task LatestLts_FromTable()
    {
        var t = await Resolver(Feed()).ResolveAsync("latest-lts", null, false, Ct);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), t.Resolved);
    }

    [Fact]
    public void LatestLtsMajor_IgnoresEndOfLifeMajors()
    {
        Assert.Equal(17, Table.LatestLtsMajor(new DateOnly(2026, 11, 5)));
        Assert.Throws<UsageException>(() => Table.LatestLtsMajor(new DateOnly(2029, 1, 1)));
    }

    [Fact]
    public async Task Tfm_FromUmbracoCmsNuspec_ThenFallbackTable()
    {
        var feed = Feed().Add("17.7.3", "net11.0");
        var fromNuspec = await Resolver(feed).ResolveAsync("17.7.3", null, false, Ct);
        Assert.Equal(NuGetFramework.Parse("net11.0"), fromNuspec.Tfm);

        var offline = new FakeMetadataSource { Offline = true };
        var fromTable = await Resolver(offline).ResolveAsync("17.7.2", null, false, Ct);
        Assert.Equal(NuGetFramework.Parse("net10.0"), fromTable.Tfm);
        Assert.Equal(NuGetVersion.Parse("17.7.2"), fromTable.Resolved);
    }

    [Fact]
    public async Task Tfm_NuspecWithoutGroups_UsesFallbackTable()
    {
        var feed = Feed().Add("17.7.4", tfm: null);
        var t = await Resolver(feed).ResolveAsync("17.7.4", null, false, Ct);
        Assert.Equal(NuGetFramework.Parse("net10.0"), t.Tfm);
    }

    [Fact]
    public async Task Offline_MajorTarget_ThrowsWithExactVersionHint()
    {
        var e = await Assert.ThrowsAsync<UsageException>(() =>
            Resolver(new FakeMetadataSource { Offline = true }).ResolveAsync("17", null, false, Ct));
        Assert.Contains("exact version", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("seventeen")]
    [InlineData("-1")]
    [InlineData("latest")]
    public async Task Garbage_Throws(string requested) =>
        await Assert.ThrowsAsync<UsageException>(() => Resolver(Feed()).ResolveAsync(requested, null, false, Ct));

    [Fact]
    public void FallbackTfm_UnknownMajor_IsNull() => Assert.Null(Table.FallbackTfm(99));

    private sealed class FixedVersionsSource(UmbracoVersionsTable table) : IUmbracoVersionsSource
    {
        public Task<UmbracoVersionsTable> GetTableAsync(CancellationToken ct) => Task.FromResult(table);
    }
}
