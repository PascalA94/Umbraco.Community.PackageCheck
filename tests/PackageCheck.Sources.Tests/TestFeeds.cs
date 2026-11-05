using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Time.Testing;
using PackageCheck.Sources.Http;
using PackageCheck.Sources.NuGet;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

/// <summary>Builds throwaway NuGet.config folders and synthetic feeds for the source tests.</summary>
public sealed class TestFeeds : IDisposable
{
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    public string Root { get; } = Directory.CreateTempSubdirectory("pc-feeds-").FullName;
    public string GlobalPackages => Path.Combine(Root, "_global");
    public string HttpCache => Path.Combine(Root, "_http");
    public string Temp => Path.Combine(Root, "_tmp");

    /// <summary>Recorded nuget.org responses: tests/Support/Http/nuget (source tree when recording, output copy when replaying).</summary>
    public static string NuGetFixtures =>
        FixtureHttpHandler.ModeFromEnvironment() == RecordMode.Record
            ? Path.Combine(RepoRoot(), "tests", "Support", "Http", "nuget")
            : Path.Combine(AppContext.BaseDirectory, "Http", "nuget");

    public FixtureHttpHandler Handler { get; } = new(NuGetFixtures, FixtureHttpHandler.ModeFromEnvironment());

    /// <summary>
    /// Writes a NuGet.config with &lt;clear/&gt; so the developer's user-level sources and mappings never leak in.
    /// </summary>
    public string WriteConfig(string relativeDir, IEnumerable<(string Key, string Url)> sources, string extraXml = "")
    {
        var dir = Path.Combine(Root, relativeDir);
        Directory.CreateDirectory(dir);
        var adds = string.Join("\n", sources.Select(s => $"""    <add key="{s.Key}" value="{s.Url}" />"""));
        // NuGet treats a second <packageSourceMapping> after one holding only <clear/> as no mapping at all, so a
        // caller's mapping replaces the default section and carries the <clear/> itself.
        var mapping = extraXml.Contains("<packageSourceMapping>")
            ? ""
            : "  <packageSourceMapping>\n    <clear />\n  </packageSourceMapping>";
        extraXml = extraXml.Replace("<packageSourceMapping>", "<packageSourceMapping>\n    <clear />");
        File.WriteAllText(Path.Combine(dir, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
            {adds}
              </packageSources>
            {mapping}
            {extraXml}
            </configuration>
            """);
        return dir;
    }

    public FeedSettings Settings(string siteDir) =>
        FeedSettings.ForSite(siteDir, GlobalPackages, HttpCache, Temp);

    public NuGetMetadataSource Source(string siteDir, TimeProvider? clock = null, Core.Facts.ArchiveLimits? limits = null) =>
        new(Settings(siteDir), new HttpPolicy(clock ?? new FakeTimeProvider()), Handler, limits);

    public static string ServiceIndex(string host, string? registrationHost = null) => $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "{{registrationHost ?? host}}/reg/", "@type": "RegistrationsBaseUrl/3.6.0" },
            { "@id": "{{host}}/flat/", "@type": "PackageBaseAddress/3.0.0" }
          ]
        }
        """;

    /// <summary>
    /// A synthetic feed at <paramref name="host"/>. Kinds: "404" (index answers, the package is absent), "401", "403",
    /// "5xx" (503 on everything), "refused" (connection refused before any response).
    /// </summary>
    public void SyntheticFeed(string host, string kind)
    {
        switch (kind)
        {
            case "404":
                Handler.Route($"{host}/v3/index.json", HttpStatusCode.OK, ServiceIndex(host));
                Handler.Route($"{host}/", HttpStatusCode.NotFound);
                break;
            case "401":
                Handler.Route($"{host}/", HttpStatusCode.Unauthorized);
                break;
            case "403":
                Handler.Route($"{host}/", HttpStatusCode.Forbidden);
                break;
            case "5xx":
                Handler.Route($"{host}/", HttpStatusCode.ServiceUnavailable);
                break;
            case "refused":
                Handler.Route($"{host}/", (_, _) => throw new HttpRequestException(
                    "Connection refused", new SocketException((int)SocketError.ConnectionRefused)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> while advancing the fake clock, so backoff delays and 30 s timeouts elapse without
    /// real sleeping. The short real yield only lets continuations run between advances.
    /// </summary>
    public static async Task<T> Drive<T>(FakeTimeProvider clock, Task<T> work)
    {
        // Recording talks to the live feed, which needs real time to answer; at 1 ms per fake half-second a fake 30 s
        // timeout would fire after ~60 ms of real time and cut live responses off.
        var realStep = FixtureHttpHandler.ModeFromEnvironment() == RecordMode.Record ? 100 : 1;
        for (var i = 0; i < 2000 && !work.IsCompleted; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(realStep);
        }
        Assert.True(work.IsCompleted, "work did not finish while the fake clock advanced");
        return await work;
    }

    public static async Task<Exception> DriveToFailure<T>(FakeTimeProvider clock, Task<T> work)
    {
        try { await Drive(clock, work); }
        catch (Exception e) { return e; }
        throw new Xunit.Sdk.XunitException("expected the call to fail");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UmbracoPackageCheck.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    public void Dispose()
    {
        Handler.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}
