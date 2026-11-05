using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PackageCheck.Core;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Inputs;
using PackageCheck.Inspect;
using PackageCheck.Sources.Cache;
using PackageCheck.Sources.Data;
using PackageCheck.Sources.Http;
using PackageCheck.Sources.Marketplace;
using PackageCheck.Sources.Overrides;

namespace PackageCheck.Cli.Commands;

/// <summary>
/// The one composition root (pitfalls P1 and P2: no static state, no service locator; preflight 1.35). Online, offline
/// and no-cache compositions are chosen here (design 7.5).
/// </summary>
public static class ServiceRegistration
{
    private static readonly TimeSpan MarketplaceMaxAge = TimeSpan.FromHours(24);

    public static ServiceProvider Build(CliEnvironment env, ScanRequest req)
    {
        if (req.Offline && req.NoCache)
            throw new UsageException("--offline reads only the cache, so it cannot be combined with --no-cache");
        if (req.Offline && req.OverridesPath is { } o && Uri.TryCreate(o, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            throw new UsageException("--overrides <url> needs the network; with --offline pass a local file");

        var cacheRoot = CacheRoot(env, req.CacheDir);
        var services = new ServiceCollection();

        services.AddSingleton(env.Clock);
        services.AddSingleton(new HttpPolicy(env.Clock));
        services.AddSingleton<RunWarnings>();
        services.AddSingleton<IProjectReader, ProjectReader>();
        services.AddSingleton<IUmbracoVersionsSource, BundledUmbracoVersionsSource>();

        FileCache? cache = req.NoCache ? null : new FileCache(cacheRoot, env.Clock);
        // With --no-cache, NuGet's own HTTP cache goes inside the run's temp folder, deleted with it (review M4 F8).
        var httpCache = cache is null ? null : Path.Combine(cacheRoot, "nuget-http");
        services.AddSingleton(new SourceMode(req.Offline, cache, req.MaxAge, httpCache, env.Handler));

        // The test handler belongs to the test, so the client never disposes it.
        services.AddSingleton(sp => new HttpClient(
            sp.GetRequiredService<HttpPolicy>().CreateHandler(env.Handler ?? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }),
            disposeHandler: env.Handler is null));

        services.AddSingleton<IMarketplaceSource>(sp =>
        {
            IMarketplaceSource m = req.Offline
                ? new OfflineMarketplaceSource()
                : new MarketplaceClient(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<RunWarnings>().Add);
            return cache is null ? m : new CachingMarketplaceSource(m, cache, req.Offline ? null : MarketplaceMaxAge);
        });
        services.AddSingleton<INupkgInspector>(_ =>
        {
            INupkgInspector i = new NupkgInspector(ArchiveLimits.Default);
            return cache is null ? i : new CachingInspector(i, cache);
        });
        services.AddSingleton<IOverrideSource>(sp =>
        {
            var sources = new List<IOverrideSource> { new BundledOverrideSource() };
            if (req.OverridesPath is { } path)
                sources.Add(new LocalOverrideSource(path, sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<RunWarnings>().Add));
            return new CompositeOverrideSource(sources);
        });
        services.AddSingleton<SiteServicesFactory>();
        services.AddSingleton<ScanOrchestrator>();

        env.ConfigureServices?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>
    /// LocalApplicationData/umbraco-package-check (design 7.5). Linux containers without a home folder give an empty
    /// LocalApplicationData; the user temp folder is the fallback there (M3 deferred minor), never the working directory.
    /// </summary>
    public static string CacheRoot(CliEnvironment env, string? cacheDir) =>
        cacheDir is not null ? Path.GetFullPath(cacheDir, env.WorkingDirectory) : env.CacheRoot ?? DefaultCacheRoot(env);

    private static string DefaultCacheRoot(CliEnvironment env)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local) || !Path.IsPathRooted(local))
            local = env.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg) ? xdg : Path.GetTempPath();
        return Path.Combine(local, "umbraco-package-check");
    }
}
