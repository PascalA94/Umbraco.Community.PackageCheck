using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Packaging;
using NuGet.Versioning;

namespace PackageCheck.Golden.Tests;

/// <summary>One pinned real package from golden-packages.json (Tasks 3, 7).</summary>
public sealed record GoldenPackage(string Id, string Version, string Sha512, string? Target, string[]? Options, GoldenExpectation? Computed,
    GoldenExpectation? Expected, string? ExpectedStatus);

public sealed record GoldenExpectation(string Verdict, string? Confidence, string? Recommended, string? HighestCompatible,
    string? LatestStable, string? PrereleaseCompatible, int? Row);

/// <summary>
/// Real nupkgs, downloaded on first use into <c>~/.cache/umbraco-package-check-tests/</c> (or <c>$GOLDEN_CACHE</c>) and
/// checked against their pinned SHA-512; a mismatch fails the test. They are never committed. The folder uses NuGet's
/// global-packages layout, so the engine opens them exactly as it opens a restored site's packages (design 7.5).
/// </summary>
public static class GoldenNupkgCache
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("GOLDEN_CACHE") is { Length: > 0 } env
        ? env
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "umbraco-package-check-tests");

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "umbraco-package-check-golden-tests" } } };

    public static IReadOnlyList<GoldenPackage> All { get; } = JsonSerializer.Deserialize<List<GoldenPackage>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden-packages.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static async Task<string> GetAsync(string id, string version, string sha512, CancellationToken ct)
    {
        var v = NuGetVersion.Parse(version);
        var path = new VersionFolderPathResolver(Root).GetPackageFilePath(id, v);
        await Gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var lower = id.ToLowerInvariant();
                var norm = v.ToNormalizedString().ToLowerInvariant();
                var bytes = await Http.GetByteArrayAsync($"https://api.nuget.org/v3-flatcontainer/{lower}/{norm}/{lower}.{norm}.nupkg", ct);
                var tmp = path + ".download";
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                File.Move(tmp, path, overwrite: true);
            }
            var actual = Convert.ToBase64String(SHA512.HashData(await File.ReadAllBytesAsync(path, ct)));
            if (actual != sha512)
            {
                File.Delete(path);
                Assert.Fail($"{id} {version}: sha512 mismatch (pinned {sha512}, got {actual})");
            }
            return path;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Every pinned nupkg, so the engine finds own, dependency and candidate packages in the folder.</summary>
    public static async Task EnsureAllAsync(CancellationToken ct)
    {
        foreach (var p in All.DistinctBy(p => $"{p.Id.ToLowerInvariant()}/{p.Version}"))
            await GetAsync(p.Id, p.Version, p.Sha512, ct);
    }
}
