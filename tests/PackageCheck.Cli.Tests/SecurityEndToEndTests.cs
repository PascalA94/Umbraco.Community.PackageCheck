using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

/// <summary>Pins design section 4 across the whole pipeline (plan Task 28c).</summary>
public class SecurityEndToEndTests
{
    private const string Contentment = "Umbraco.Community.Contentment";

    private static Dictionary<string, string> HashTree(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public async Task ScannedFolder_IsUnchanged()
    {
        var site = TestSites.Create("17.7.2", (Contentment, "6.2.1"), ("Astroboard", "1.1.3"));
        var before = HashTree(site);

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", site, "--target", "17.7.2", "--transitive", "--format", "md");

        Assert.NotEqual(2, r.Code);
        Assert.Equal(before, HashTree(site));
    }

    [Fact]
    public async Task PlantedCredential_AbsentFromAllOutputs()
    {
        const string token = "PLANTED-TOKEN-123"; // gitleaks:allow
        const string userinfoToken = "PLANTEDUSERINFO"; // gitleaks:allow
        var site = TestSites.Create("17.7.2", (Contentment, "6.2.1"));
        var gp = Directory.CreateTempSubdirectory("pc-cli-gp-").FullName;
        File.WriteAllText(Path.Combine(site, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config><add key="globalPackagesFolder" value="{gp}" /></config>
              <packageSources>
                <clear />
                <add key="private" value="https://ci:{userinfoToken}@pkgs.example.test/v3/index.json" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceCredentials>
                <private><add key="Username" value="ci" /><add key="ClearTextPassword" value="{token}" /></private>
              </packageSourceCredentials>
            </configuration>
            """);
        var cache = Directory.CreateTempSubdirectory("pc-cli-cred-cache-").FullName;
        var handler = TestSites.Handler().Route("https://pkgs.example.test/", HttpStatusCode.Unauthorized);
        var mdFile = Path.Combine(site, "..", Path.GetFileName(site) + "-report.md");

        var outputs = new List<string>();
        foreach (var args in new[]
                 {
                     new[] { "scan", site, "--target", "17.7.2", "--no-inspect", "--cache-dir", cache, "--verbose" },
                     ["scan", site, "--target", "17.7.2", "--no-inspect", "--cache-dir", cache, "--format", "json"],
                     ["scan", site, "--target", "17.7.2", "--no-inspect", "--cache-dir", cache, "--format", "md", "-o", mdFile],
                 })
        {
            var r = await CliHarness.RunWithAsync(handler, cache, null, args);
            outputs.AddRange([r.Stdout, r.Stderr, r.Console]);
        }
        outputs.Add(File.ReadAllText(mdFile));
        outputs.AddRange(Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        outputs.AddRange(Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories));

        Assert.Contains(handler.Requests, u => u.Contains("@pkgs.example.test/", StringComparison.Ordinal));
        Assert.Empty(handler.Misses);
        foreach (var text in outputs)
        {
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            Assert.DoesNotContain(userinfoToken, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task MarkdownInjection_InCsprojId_EscapedInMd()
    {
        var site = TestSites.Create("17.7.2", ("&lt;img src=x onerror=alert(1)&gt;", "1.0.0"));

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", site, "--target", "17.7.2", "--no-inspect", "--format", "md");

        Assert.Equal(0, r.Code);
        Assert.Contains("&lt;img", r.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", r.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsafeArchive_EndToEnd_IsUnknown()
    {
        var gp = Directory.CreateTempSubdirectory("pc-cli-gp-unsafe-").FullName;
        var nupkg = NupkgBuilder.Create(Contentment, "6.2.1")
            .WithRawEntryName("../../pc-zip-slip-planted.txt")
            .WithFile("App_Plugins/Contentment/umbraco-package.json", "{}")
            .Build();
        var folder = Path.Combine(gp, "umbraco.community.contentment", "6.2.1");
        Directory.CreateDirectory(folder);
        File.Copy(nupkg, Path.Combine(folder, "umbraco.community.contentment.6.2.1.nupkg"));
        var site = TestSites.Create("17.7.2", gp, (Contentment, "6.2.1"));

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", site, "--target", "17.7.2", "--no-cache", "--format", "json");

        var pkg = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("sites")[0].GetProperty("packages")[0];
        Assert.Equal("unknown", pkg.GetProperty("verdict").GetString());
        Assert.Equal("unsafe-archive", pkg.GetProperty("reason").GetString());
        foreach (var dir in new[] { gp, folder, Path.GetTempPath(), Directory.GetCurrentDirectory(), Path.GetDirectoryName(folder)!, Path.GetDirectoryName(Path.GetDirectoryName(folder))! })
            Assert.False(File.Exists(Path.Combine(dir, "pc-zip-slip-planted.txt")), dir);
    }
}
