using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Sources.Cache;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

public sealed class FileCacheTests : IDisposable
{
    private const string Feed = "https://api.nuget.org/v3/index.json";
    private readonly string _root = Directory.CreateTempSubdirectory("pc-cache-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private FileCache Cache() => new(_root, _clock);

    private static CacheKey Key(string id = "Pkg", string? version = "1.0.0") => new("versions", Feed, id, version, null);

    [Fact]
    public void Write_CreatesMarker_SoClearKnowsTheFolder()
    {
        var cache = Cache();
        cache.Write(Key(), new[] { "a" });

        Assert.True(File.Exists(Path.Combine(_root, FileCache.MarkerFileName)));
        Assert.True(cache.IsCacheFolder);
    }

    /// <summary>Cache files other than the marker that identifies the folder (security review M4-S1).</summary>
    private string[] EntryFiles() => [.. Directory.GetFiles(_root).Where(f => Path.GetFileName(f) != FileCache.MarkerFileName)];

    [Fact]
    public void Write_IsAtomic_TempThenRename()
    {
        var cache = Cache();
        cache.Write(Key(), new[] { "a" });
        var files = EntryFiles();
        var file = Assert.Single(files);
        Assert.EndsWith(".json", file);
        Assert.DoesNotContain(files, f => f.EndsWith(".tmp"));
        Assert.True(cache.TryRead<string[]>(Key(), null, out var v));
        Assert.Equal(["a"], v);
    }

    [Fact]
    public void FileName_IsSha256OfNormalisedKey_IdCaseInsensitive()
    {
        var cache = Cache();
        cache.Write(Key("Umbraco.Community.Contentment"), new[] { "a" });
        Assert.True(cache.TryRead<string[]>(Key("umbraco.community.contentment"), null, out _));
        Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(Assert.Single(EntryFiles())));
    }

    [Fact]
    public void CorruptCacheEntry_IsAMiss()
    {
        var cache = Cache();
        cache.Write(Key(), new[] { "a" });
        var file = Assert.Single(EntryFiles());
        File.WriteAllBytes(file, [0xFF, 0x00, 0x7B, 0x22]);
        Assert.False(cache.TryRead<string[]>(Key(), null, out _));
        cache.Write(Key(), new[] { "b" });
        Assert.True(cache.TryRead<string[]>(Key(), null, out var v));
        Assert.Equal(["b"], v);
    }

    [Fact]
    public async Task ParallelRuns_ShareCacheSafely()
    {
        var a = Cache();
        var b = Cache();
        async Task Hammer(FileCache c) => await Task.Run(() =>
        {
            for (var round = 0; round < 20; round++)
            for (var i = 0; i < 50; i++)
            {
                c.Write(Key($"pkg{i}"), new[] { $"v{i}", new string('x', 64 * 1024) }); // big enough for a torn write to show
                if (c.TryRead<string[]>(Key($"pkg{i}"), null, out var v)) Assert.Equal($"v{i}", v[0]);
            }
        }, Ct);
        await Task.WhenAll(Hammer(a), Hammer(b));
        for (var i = 0; i < 50; i++) Assert.True(a.TryRead<string[]>(Key($"pkg{i}"), null, out _));
        // Review M3 (Q1): a torn write would read as a corrupt entry; atomic rename means a reader never sees one.
        Assert.Equal((0, 0), (a.CorruptReads, b.CorruptReads));
    }

    [Fact]
    public async Task ParallelRuns_TwoProcesses_ShareCacheSafely()
    {
        var hammer = Path.Combine(AppContext.BaseDirectory, "CacheHammer.dll");
        Assert.True(File.Exists(hammer), $"CacheHammer.dll not built next to the tests: {hammer}");
        Process Start() => Process.Start(new ProcessStartInfo("dotnet", [hammer, _root, "20"]) { RedirectStandardError = true })!;
        using var p1 = Start();
        using var p2 = Start();
        await Task.WhenAll(p1.WaitForExitAsync(Ct), p2.WaitForExitAsync(Ct));
        Assert.Equal((0, ""), (p1.ExitCode, await p1.StandardError.ReadToEndAsync(Ct)));
        Assert.Equal((0, ""), (p2.ExitCode, await p2.StandardError.ReadToEndAsync(Ct)));
        var cache = Cache();
        for (var i = 0; i < 50; i++)
        {
            Assert.True(cache.TryRead<string[]>(new CacheKey("hammer", "https://feed.example/v3/index.json", $"pkg{i}", "1.0.0", null), null, out var v));
            Assert.Equal($"value-{i}", v[0]);
        }
    }

    [Fact]
    public void StaleTmpFiles_IgnoredAndCleaned()
    {
        var cache = Cache();
        cache.Write(Key(), new[] { "a" });
        var name = Path.GetFileNameWithoutExtension(Assert.Single(EntryFiles()));
        var stale = Path.Combine(_root, $"{name}.{Guid.NewGuid():N}.tmp");
        var fresh = Path.Combine(_root, $"{name}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(stale, "garbage");
        File.WriteAllText(fresh, "garbage");
        File.SetLastWriteTimeUtc(stale, _clock.GetUtcNow().UtcDateTime.AddHours(-2));
        File.SetLastWriteTimeUtc(fresh, _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        Assert.True(cache.TryRead<string[]>(Key(), null, out var v));
        Assert.Equal(["a"], v);
        Assert.Equal(1, cache.Info().Entries);

        // The next run's first write cleans up.
        Cache().Write(Key("Other"), new[] { "b" });
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Expired_IsAMiss()
    {
        var cache = Cache();
        cache.Write(Key(), new[] { "a" });
        _clock.Advance(TimeSpan.FromHours(5));
        Assert.True(cache.TryRead<string[]>(Key(), TimeSpan.FromHours(6), out _));
        _clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        Assert.False(cache.TryRead<string[]>(Key(), TimeSpan.FromHours(6), out _));
        Assert.True(cache.TryRead<string[]>(Key(), null, out _)); // no max age (offline) still serves it
    }

    [Fact]
    public void Info_And_Clear()
    {
        var cache = Cache();
        cache.Write(Key("a"), new[] { "a" });
        cache.Write(Key("b"), new[] { "b" });
        var info = cache.Info();
        Assert.Equal((_root, 2), (info.Root, info.Entries));
        Assert.True(info.Bytes > 0);
        cache.Clear();
        Assert.Equal(0, cache.Info().Entries);
        Assert.False(cache.TryRead<string[]>(Key("a"), null, out _));
    }

    [Fact]
    public void MissingRoot_IsCreatedOnWrite_AndAMissOnRead()
    {
        var cache = new FileCache(Path.Combine(_root, "nested", "dir"), _clock);
        Assert.False(cache.TryRead<string[]>(Key(), null, out _));
        Assert.Equal(0, cache.Info().Entries);
        cache.Write(Key(), new[] { "a" });
        Assert.True(cache.TryRead<string[]>(Key(), null, out _));
    }

    [Fact]
    public async Task CacheFiles_NeverContainCredentials()
    {
        const string token = "s3cr3t-T0KEN-planted"; // gitleaks:allow
        var feedWithUserInfo = $"https://user:{token}@feed.example/v3/index.json";
        var cache = Cache();
        var inner = new StubMetadataSource();
        var source = new CachingMetadataSource(inner, cache, TimeSpan.FromHours(6), feedWithUserInfo);
        await source.GetVersionsAsync("Pkg", Ct);
        await source.GetDetailsAsync("Pkg", NuGetVersion.Parse("1.0.0"), Ct);
        Assert.DoesNotContain(token, new CacheKey("details", feedWithUserInfo, "Pkg", "1.0.0", null).ToString());
        // S1: a token in the path never reaches a key either.
        Assert.DoesNotContain("PATHTOKEN", new CacheKey("details", "https://dl.cloudsmith.io/PATHTOKEN/o/r/nuget/index.json", "Pkg", "1.0.0", null).ToString());
        Assert.NotEqual(new CacheKey("d", "https://h.test/a/index.json", "P", null, null).Normalised,
            new CacheKey("d", "https://h.test/b/index.json", "P", null, null).Normalised); // two feeds on one host stay apart
        Assert.NotEmpty(Directory.GetFiles(_root));
        foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(token, await File.ReadAllTextAsync(f, Ct));
    }

    [Fact]
    public async Task CachingMetadataSource_ServesSecondCallFromCache_AndRoundTripsDetails()
    {
        var inner = new StubMetadataSource();
        var source = new CachingMetadataSource(inner, Cache(), TimeSpan.FromHours(6), Feed);
        var first = await source.GetDetailsAsync("Pkg", NuGetVersion.Parse("1.0.0"), Ct);

        var again = new CachingMetadataSource(inner, Cache(), TimeSpan.FromHours(6), Feed);
        var second = await again.GetDetailsAsync("Pkg", NuGetVersion.Parse("1.0.0"), Ct);
        var versions = await again.GetVersionsAsync("Pkg", Ct);
        await again.GetVersionsAsync("Pkg", Ct);

        Assert.Equal(1, inner.DetailCalls);
        Assert.Equal(1, inner.VersionCalls);
        Assert.Equal(["1.0.0", "2.0.0"], versions.Versions.Select(v => v.ToNormalizedString()));
        Assert.Equal(first!.Identity, second!.Identity);
        Assert.Equal(first.Published, second.Published);
        Assert.Equal(first.Listed, second.Listed);
        var dep = Assert.Single(Assert.Single(second.DependencyGroups).Packages);
        Assert.Equal(("Umbraco.Cms.Core", "[17.0.0, 18.0.0)"), (dep.Id, dep.VersionRange.ToString()));
        Assert.Equal("net10.0", second.DependencyGroups[0].TargetFramework.GetShortFolderName());
        Assert.Equal(("Other.Pkg", "Legacy"), (second.Deprecation!.AlternateId, second.Deprecation.Reasons[0]));
    }

    // Review M3 (Q4): valid JSON of the wrong shape (another tool version, a hand edit) used to come back as a hit with
    // null members and crash later. It is a miss, refetched and overwritten.
    [Fact]
    public async Task WrongShapeEntries_AreMisses_NotCrashes()
    {
        var cache = Cache();
        cache.Write(new CacheKey("versions", Feed, "Pkg", null, null), new { other = 1 });
        cache.Write(new CacheKey("details", Feed, "Pkg", "1.0.0", null), new { other = 1 });
        var inner = new StubMetadataSource();
        var source = new CachingMetadataSource(inner, cache, TimeSpan.FromHours(6), Feed);
        Assert.Equal(2, (await source.GetVersionsAsync("Pkg", Ct)).Versions.Count);
        Assert.NotNull(await source.GetDetailsAsync("Pkg", NuGetVersion.Parse("1.0.0"), Ct));
        Assert.Equal((1, 1), (inner.VersionCalls, inner.DetailCalls));

        var nupkg = NupkgBuilder.Create("Pkg", "1.0.0").Build();
        var id = new PackageIdentity("Pkg", NuGetVersion.Parse("1.0.0"));
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(nupkg)));
        cache.Write(new CacheKey("inspect", "", "Pkg", "1.0.0", $"{hash}|v{CachingInspector.InspectionCacheVersion}"), new { other = 1 });
        var inspector = new CountingInspector();
        var r = await new CachingInspector(inspector, cache).InspectAsync(id, new NupkgHandle(nupkg, false, 1), Ct);
        Assert.Equal((1, "Pkg"), (inspector.Calls, r.PackageId));
    }

    [Fact]
    public async Task CachingMetadataSource_DoesNotCacheMissingDetails()
    {
        var inner = new StubMetadataSource();
        var source = new CachingMetadataSource(inner, Cache(), TimeSpan.FromHours(6), Feed);
        Assert.Null(await source.GetDetailsAsync("Pkg", NuGetVersion.Parse("9.9.9"), Ct));
        Assert.Null(await source.GetDetailsAsync("Pkg", NuGetVersion.Parse("9.9.9"), Ct));
        Assert.Equal(2, inner.DetailCalls);
    }

    [Fact]
    public async Task InspectionCacheVersionBump_DiscardsOldEvidence()
    {
        var nupkg = NupkgBuilder.Create("Pkg", "1.0.0").WithFile("staticwebassets/umbraco-package.json", "{}").Build();
        var handle = new NupkgHandle(nupkg, false, new FileInfo(nupkg).Length);
        var id = new PackageIdentity("Pkg", NuGetVersion.Parse("1.0.0"));
        var inner = new CountingInspector();

        await new CachingInspector(inner, Cache()).InspectAsync(id, handle, Ct);
        var cached = await new CachingInspector(inner, Cache()).InspectAsync(id, handle, Ct);
        Assert.Equal(1, inner.Calls);
        Assert.True(cached.HasUmbracoPackageJson);
        Assert.Equal(["n"], cached.Notes);

        await new CachingInspector(inner, Cache(), cacheVersion: CachingInspector.InspectionCacheVersion + 1).InspectAsync(id, handle, Ct);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task CachingInspector_KeysOnContentHash()
    {
        var id = new PackageIdentity("Pkg", NuGetVersion.Parse("1.0.0"));
        var a = NupkgBuilder.Create("Pkg", "1.0.0").WithFile("a.txt", "a").Build();
        var b = NupkgBuilder.Create("Pkg", "1.0.0").WithFile("b.txt", "b").Build();
        var inner = new CountingInspector();
        await new CachingInspector(inner, Cache()).InspectAsync(id, new NupkgHandle(a, false, 1), Ct);
        await new CachingInspector(inner, Cache()).InspectAsync(id, new NupkgHandle(b, false, 1), Ct);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task CachingMarketplace_CachesListings_NotNulls()
    {
        var inner = new StubMarketplace();
        var cache = Cache();
        var v = NuGetVersion.Parse("17.7.2");
        await new CachingMarketplaceSource(inner, cache, TimeSpan.FromHours(24)).GetAsync("Listed", v, Ct);
        var hit = await new CachingMarketplaceSource(inner, cache, TimeSpan.FromHours(24)).GetAsync("Listed", v, Ct);
        await new CachingMarketplaceSource(inner, cache, TimeSpan.FromHours(24)).GetAsync("Missing", v, Ct);
        await new CachingMarketplaceSource(inner, cache, TimeSpan.FromHours(24)).GetAsync("Missing", v, Ct);
        Assert.Equal(("https://marketplace.umbraco.com/package/listed", "17.1.0"), (hit!.Url, hit.LatestForTarget!.ToNormalizedString()));
        Assert.Equal(3, inner.Calls);
    }

    internal sealed class StubMetadataSource : IPackageMetadataSource
    {
        public int VersionCalls { get; private set; }
        public int DetailCalls { get; private set; }

        public Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct)
        {
            VersionCalls++;
            return Task.FromResult(new PackageVersions(id, [NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0")]));
        }

        public Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct)
        {
            DetailCalls++;
            if (v.Major == 9) return Task.FromResult<PackageDetails?>(null);
            var group = new global::NuGet.Packaging.PackageDependencyGroup(global::NuGet.Frameworks.NuGetFramework.Parse("net10.0"),
                [new PackageDependency("Umbraco.Cms.Core", VersionRange.Parse("[17.0.0, 18.0.0)"))]);
            return Task.FromResult<PackageDetails?>(new PackageDetails(new PackageIdentity(id, v), [group],
                new DeprecationInfo(["Legacy"], "Other.Pkg", VersionRange.Parse("[2.0.0, )")),
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), Listed: true));
        }

        public Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct) => Task.FromResult<NupkgHandle?>(null);
    }

    private sealed class CountingInspector : INupkgInspector
    {
        public int Calls { get; private set; }

        public Task<InspectionResult> InspectAsync(PackageIdentity id, NupkgHandle nupkg, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new InspectionResult(id.Id, id.Version.ToNormalizedString(), true, "App_Plugins/Pkg", false, false, false,
                true, ["net10.0"], "hash", ["staticwebassets/umbraco-package.json"], ["n"], [new LibScan("net10.0", false, false)]));
        }
    }

    private sealed class StubMarketplace : IMarketplaceSource
    {
        public int Calls { get; private set; }

        public Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(id == "Listed"
                ? new MarketplaceInfo("https://marketplace.umbraco.com/package/listed", false, null, false, NuGetVersion.Parse("17.1.0"), "Listed")
                : null);
        }
    }
}
