using Microsoft.Extensions.Time.Testing;
using PackageCheck.Sources.Cache;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

public class CacheCommandTests
{
    private static string CacheWithEntries(int n)
    {
        var parent = Directory.CreateTempSubdirectory("pc-cache-cmd-").FullName;
        var root = Path.Combine(parent, "cache");
        var cache = new FileCache(root, new FakeTimeProvider(CliHarness.RecordedOn));
        for (var i = 0; i < n; i++) cache.Write(new CacheKey("test", "feed", $"id{i}", "1.0.0", null), new { Value = i });
        return root;
    }

    [Fact]
    public async Task CacheInfo_ShowsRoot()
    {
        var root = CacheWithEntries(3);

        var r = await CliHarness.RunAsync(null, "cache", "info", "--cache-dir", root);

        Assert.Equal(0, r.Code);
        Assert.Contains(root, r.Stdout, StringComparison.Ordinal);
        Assert.Contains("entries: 3", r.Stdout, StringComparison.Ordinal);
        Assert.Matches(@"size: \d+ bytes", r.Stdout);
    }

    [Fact]
    public async Task CacheClear_EmptiesOnlyCacheRoot()
    {
        var root = CacheWithEntries(3);
        var sibling = Path.Combine(Path.GetDirectoryName(root)!, "keep.json");
        File.WriteAllText(sibling, "{}");

        var r = await CliHarness.RunAsync(null, "cache", "clear", "--cache-dir", root);

        Assert.Equal(0, r.Code);
        Assert.True(File.Exists(sibling));
        // Only the marker that identifies the folder as a cache stays (security review M4-S1).
        Assert.Equal([FileCache.MarkerFileName], Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
    }

    [Fact]
    public async Task CacheClear_KeepsFilesTheToolDidNotWrite()
    {
        // Security review M4-S1: `--cache-dir .` in a project root must never delete the project's own json files.
        var root = CacheWithEntries(2);
        var foreign = new[] { "package.json", "appsettings.Development.json", "notes.tmp" }.Select(f => Path.Combine(root, f)).ToList();
        foreach (var f in foreign) File.WriteAllText(f, "{}");

        var r = await CliHarness.RunAsync(null, "cache", "clear", "--cache-dir", root);

        Assert.Equal(0, r.Code);
        Assert.All(foreign, f => Assert.True(File.Exists(f), f));
        Assert.DoesNotContain(Directory.EnumerateFiles(root, "*.json"), f => !foreign.Contains(f));
    }

    [Fact]
    public async Task CacheClear_RefusesFolderThatIsNotACache()
    {
        var folder = Directory.CreateTempSubdirectory("pc-not-a-cache-").FullName;
        Directory.CreateDirectory(Path.Combine(folder, "nuget-http"));
        File.WriteAllText(Path.Combine(folder, "nuget-http", "keep.txt"), "x");
        var hashName = Path.Combine(folder, new string('a', 64) + ".json");
        File.WriteAllText(hashName, "{}");

        var r = await CliHarness.RunAsync(null, "cache", "clear", "--cache-dir", folder);

        Assert.Equal(2, r.Code);
        Assert.Contains("not an umbraco-package-check cache", r.Stderr, StringComparison.Ordinal);
        Assert.True(File.Exists(hashName));
        Assert.True(File.Exists(Path.Combine(folder, "nuget-http", "keep.txt")));
    }

    [Fact]
    public async Task CacheInfo_CountsOnlyCacheEntries()
    {
        var root = CacheWithEntries(3);
        File.WriteAllText(Path.Combine(root, "package.json"), "{}");

        var r = await CliHarness.RunAsync(null, "cache", "info", "--cache-dir", root);

        Assert.Contains("entries: 3", r.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cache_UnknownAction_Exit2()
    {
        var r = await CliHarness.RunAsync(null, "cache", "wipe");

        Assert.Equal(2, r.Code);
    }
}
