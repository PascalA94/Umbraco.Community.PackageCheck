using Microsoft.Extensions.Time.Testing;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Core.Verdicts;
using PackageCheck.Inspect;
using PackageCheck.Sources.Data;
using PackageCheck.Sources.Http;
using PackageCheck.Sources.Marketplace;
using PackageCheck.Sources.NuGet;
using PackageCheck.Tests.Support;

namespace PackageCheck.Golden.Tests;

/// <summary>
/// The engine composed from production types only, over recorded HTTP (ruling R20: goldens never hit live feeds) and the
/// hash-pinned nupkgs as the global packages folder. The real composition root arrives in Task 28a; this helper must not
/// grow logic the CLI lacks. The clock is pinned to the recording date so H18 and override expiry are stable.
/// </summary>
public sealed class Engine
{
    public static readonly DateTimeOffset RecordedOn = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<Engine> ComputedEngine = new(() => Create(OverrideSet.Empty));

    /// <summary>No overrides: every golden proves the computed verdict (global constraints).</summary>
    public static Engine Computed => ComputedEngine.Value;

    /// <summary>tests/Support/Http/golden: the source tree when recording, the output copy when replaying.</summary>
    public static string Fixtures => FixtureHttpHandler.ModeFromEnvironment() == RecordMode.Record
        ? Path.Combine(RepoRoot(), "tests", "Support", "Http", "golden")
        : Path.Combine(AppContext.BaseDirectory, "Http", "golden");

    public static FixtureHttpHandler Handler { get; } = new(Fixtures, FixtureHttpHandler.ModeFromEnvironment());

    private static readonly UmbracoVersionsTable Versions = UmbracoVersionsTable.Parse(BundledData.Open(BundledData.UmbracoVersionsResource));

    private readonly VerdictEngine _engine;

    private Engine(VerdictEngine engine) => _engine = engine;

    public static Engine Create(OverrideSet overrides)
    {
        var site = Directory.CreateTempSubdirectory("pc-golden-").FullName;
        // <clear/> keeps the developer's own feeds and mappings out of the run.
        File.WriteAllText(Path.Combine(site, "NuGet.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
              <packageSourceMapping><clear /></packageSourceMapping>
            </configuration>
            """);
        var clock = new FakeTimeProvider(RecordedOn);
        var policy = new HttpPolicy(clock);
        var feeds = FeedSettings.ForSite(site, GoldenNupkgCache.Root, Path.Combine(site, "_http"), Path.Combine(site, "_tmp"));
        var meta = new NuGetMetadataSource(feeds, policy, Handler);
        var marketplace = new MarketplaceClient(new HttpClient(policy.CreateHandler(Handler)));
        var collector = new FactsCollector(meta, marketplace, new NupkgInspector(ArchiveLimits.Default));
        return new Engine(new VerdictEngine(collector, overrides, clock, HeuristicSet.All(Versions)));
    }

    public async Task<PackageVerdict> EvaluateAsync(SitePackage pkg, TargetVersion target, ScanOptions o)
    {
        await GoldenNupkgCache.EnsureAllAsync(TestContext.Current.CancellationToken);
        return await _engine.EvaluateAsync(pkg, target, o, TestContext.Current.CancellationToken);
    }

    public static SitePackage Pkg(string id, string version) =>
        new(new PackageRef(id, version, NuGetVersion.Parse(version), Direct: true, UnresolvedReason: null), ["Site"]);

    /// <summary>An exact target; its TFM comes from the bundled versions table, as the resolver would give it.</summary>
    public static TargetVersion Target(string version)
    {
        var v = NuGetVersion.Parse(version);
        return new TargetVersion(version, v, Versions.FallbackTfm(v.Major)!, v.IsPrerelease);
    }

    public static ScanOptions Options(IEnumerable<string>? flags)
    {
        var f = flags?.ToList() ?? [];
        return new ScanOptions(Inspect: !f.Contains("--no-inspect"), AllowPrerelease: f.Contains("--prerelease"), Verbose: false);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UmbracoPackageCheck.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
