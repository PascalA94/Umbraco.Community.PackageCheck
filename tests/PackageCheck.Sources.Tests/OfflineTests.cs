using Microsoft.Extensions.Time.Testing;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Core.Verdicts;
using PackageCheck.Sources.Cache;
using PackageCheck.Sources.Http;
using PackageCheck.Sources.NuGet;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

public sealed class OfflineTests : IDisposable
{
    private const string Contentment = "Umbraco.Community.Contentment";
    private readonly TestFeeds _feeds = new();
    private readonly string _cacheRoot = Directory.CreateTempSubdirectory("pc-offline-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _feeds.Dispose();
        Directory.Delete(_cacheRoot, recursive: true);
    }

    private static readonly TargetVersion Target = new("17.7.2", NuGetVersion.Parse("17.7.2"), global::NuGet.Frameworks.NuGetFramework.Parse("net10.0"), false);
    private static readonly ScanOptions Metadata = new(Inspect: false, AllowPrerelease: false, Verbose: false);

    private static SitePackage Site(string version) =>
        new(new PackageRef(Contentment, version, NuGetVersion.Parse(version), Direct: true, UnresolvedReason: null), ["Site"]);

    private Task<PackageVerdict> Judge(IPackageMetadataSource source) =>
        new VerdictEngine(new FactsCollector(source), OverrideSet.Empty, _clock).EvaluateAsync(Site("6.2.1"), Target, Metadata, Ct);

    private IPackageMetadataSource Offline() =>
        new CachingMetadataSource(new OfflineMetadataSource(_feeds.GlobalPackages), new FileCache(_cacheRoot, _clock), maxAge: null, TestFeeds.NuGetOrg);

    [Fact]
    public async Task Offline_WarmCache_HttpHandlerNeverHit()
    {
        var site = _feeds.WriteConfig("site", [("nuget.org", TestFeeds.NuGetOrg)]);
        using var online = _feeds.Source(site, _clock);
        var warm = await Judge(new CachingMetadataSource(online, new FileCache(_cacheRoot, _clock), TimeSpan.FromHours(6), TestFeeds.NuGetOrg));
        Assert.Equal(Verdict.Ok, warm.Verdict);
        var onlineRequests = _feeds.Handler.RequestCount;
        Assert.True(onlineRequests > 0);

        _clock.Advance(TimeSpan.FromDays(30)); // offline ignores age
        var offline = await Judge(Offline());

        Assert.Equal(onlineRequests, _feeds.Handler.RequestCount);
        Assert.Equal((warm.Verdict, warm.Confidence), (offline.Verdict, offline.Confidence));
        Assert.Equal(warm.Evidence.Select(e => e.Detail), offline.Evidence.Select(e => e.Detail));
    }

    [Fact]
    public async Task Offline_ColdCache_IsUnknownOffline()
    {
        var v = await Judge(Offline());
        Assert.Equal((Verdict.Unknown, (UnknownReason?)UnknownReason.Offline), (v.Verdict, v.Reason));
    }

    [Fact]
    public async Task Offline_ServesNupkgFromGlobalPackagesFolderOnly()
    {
        var source = new OfflineMetadataSource(_feeds.GlobalPackages);
        Assert.Null(await source.OpenNupkgAsync("Pkg", NuGetVersion.Parse("1.0.0"), Ct));

        var dir = Path.Combine(_feeds.GlobalPackages, "pkg", "1.0.0");
        Directory.CreateDirectory(dir);
        File.Copy(NupkgBuilder.Create("Pkg", "1.0.0").Build(), Path.Combine(dir, "pkg.1.0.0.nupkg"));
        await using var handle = await source.OpenNupkgAsync("Pkg", NuGetVersion.Parse("1.0.0"), Ct);
        Assert.NotNull(handle);
        Assert.False(handle.IsTemporary);

        var ex = await Assert.ThrowsAsync<PackageSourceException>(() => source.GetVersionsAsync("Pkg", Ct));
        Assert.Equal(UnknownReason.Offline, ex.Reason);
        await Assert.ThrowsAsync<PackageSourceException>(() => source.GetDetailsAsync("Pkg", NuGetVersion.Parse("1.0.0"), Ct));
    }

    [Fact]
    public async Task OfflineMarketplace_IsCacheOrNull()
    {
        var cache = new FileCache(_cacheRoot, _clock);
        var listing = new MarketplaceInfo("https://marketplace.umbraco.com/package/x", false, null, true, null, "X");
        var online = new CachingMarketplaceSource(new FixedMarketplace(listing), cache, TimeSpan.FromHours(24));
        await online.GetAsync("X", Target.RangeVersion, Ct);
        _clock.Advance(TimeSpan.FromDays(30));

        var offline = new CachingMarketplaceSource(OfflineMarketplaceSource.Instance, cache, maxAge: null);
        Assert.Equal(listing.Url, (await offline.GetAsync("X", Target.RangeVersion, Ct))?.Url);
        Assert.Null(await offline.GetAsync("Y", Target.RangeVersion, Ct));
    }

    private sealed class FixedMarketplace(MarketplaceInfo info) : IMarketplaceSource
    {
        public Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct) => Task.FromResult<MarketplaceInfo?>(info);
    }
}
