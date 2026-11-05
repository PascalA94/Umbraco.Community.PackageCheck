using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NuGet.Configuration;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Core.Verdicts;
using PackageCheck.Sources.Cache;
using PackageCheck.Sources.Http;
using PackageCheck.Sources.NuGet;

namespace PackageCheck.Cli.Commands;

/// <summary>The engine for one feed set: its metadata source, target resolver and verdict engine.</summary>
public sealed record SiteServices(IPackageMetadataSource Metadata, TargetResolver Resolver, IVerdictEngine Engine);

/// <summary>How the run reaches packages; chosen once per run by <see cref="ServiceRegistration"/>.</summary>
/// <param name="HttpCacheFolder">Null with --no-cache: the factory then uses a folder inside its own run temp folder.</param>
public sealed record SourceMode(bool Offline, FileCache? Cache, TimeSpan MaxAge, string? HttpCacheFolder, HttpMessageHandler? Handler);

/// <summary>
/// Builds one <see cref="SiteServices"/> per distinct feed set (design 7.3: feeds come from the NuGet.config nearest
/// each site). Sites with the same feeds share one engine, so a package version used by several sites is fetched once.
/// Owns the run's temp folder and every NuGet source it creates.
/// </summary>
public sealed class SiteServicesFactory(
    SourceMode mode,
    HttpPolicy policy,
    IMarketplaceSource marketplace,
    INupkgInspector inspector,
    IUmbracoVersionsSource versions,
    TimeProvider clock) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<SiteServices>>> _byFeeds = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<IDisposable> _owned = [];
    private readonly Lazy<string> _runTemp = new(() => Directory.CreateTempSubdirectory("umbraco-package-check-").FullName);

    public Task<SiteServices> ForAsync(string sitePath, OverrideSet overrides)
    {
        var dir = Directory.Exists(sitePath) ? sitePath : Path.GetDirectoryName(Path.GetFullPath(sitePath))!;
        var settings = Settings.LoadDefaultSettings(dir);
        var key = FeedKey(settings);
        return _byFeeds.GetOrAdd(key, k => new Lazy<Task<SiteServices>>(() => CreateAsync(settings, k, overrides))).Value;
    }

    private async Task<SiteServices> CreateAsync(ISettings settings, string key, OverrideSet overrides)
    {
        // Security review M3 (S6): the run's own 0700 temp folder, never a predictable path; deleted on Dispose.
        var temp = Directory.CreateDirectory(Path.Combine(_runTemp.Value, key[..12])).FullName;
        var feeds = FeedSettings.FromSettings(settings, httpCacheFolder: mode.HttpCacheFolder ?? Path.Combine(_runTemp.Value, "nuget-http"), tempFolder: temp);

        IPackageMetadataSource meta;
        if (mode.Offline)
        {
            meta = new OfflineMetadataSource(feeds.GlobalPackagesFolder);
        }
        else
        {
            var nuget = new NuGetMetadataSource(feeds, policy, mode.Handler);
            _owned.Add(nuget);
            meta = nuget;
        }
        // The feed key is hashed into every cache key, so two feed sets never share entries (M3 deferred: per-feed keys).
        if (mode.Cache is { } cache) meta = new CachingMetadataSource(meta, cache, mode.Offline ? null : mode.MaxAge, key);

        var table = await versions.GetTableAsync(CancellationToken.None);
        var collector = new FactsCollector(meta, marketplace, inspector);
        var engine = new VerdictEngine(collector, overrides, clock, HeuristicSet.All(table));
        return new SiteServices(meta, new TargetResolver(meta, versions, clock), engine);
    }

    /// <summary>
    /// Identity of a feed set: enabled sources (name, URL, a hash of any credentials), source mapping and the global
    /// packages folder. Hashed, so neither URLs nor credentials reach a cache file name or a log.
    /// </summary>
    private static string FeedKey(ISettings settings)
    {
        var sb = new StringBuilder();
        foreach (var s in new PackageSourceProvider(settings).LoadPackageSources().Where(s => s.IsEnabled))
            sb.Append(s.Name).Append('|').Append(s.Source).Append('|')
              .Append(s.Credentials is { } c ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{c.Username}\n{c.PasswordText}"))) : "")
              .Append('\n');
        foreach (var item in new PackageSourceMappingProvider(settings).GetPackageSourceMappingItems().OrderBy(i => i.Key, StringComparer.Ordinal))
            sb.Append("map|").Append(item.Key).Append('|').AppendJoin(',', item.Patterns.Select(p => p.Pattern).Order(StringComparer.Ordinal)).Append('\n');
        sb.Append("gp|").Append(SettingsUtility.GetGlobalPackagesFolder(settings));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public void Dispose()
    {
        foreach (var d in _owned) d.Dispose();
        if (_runTemp.IsValueCreated)
        {
            try { Directory.Delete(_runTemp.Value, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
