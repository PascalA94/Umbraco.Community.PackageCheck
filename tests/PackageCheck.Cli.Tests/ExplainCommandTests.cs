using NuGet.Versioning;
using PackageCheck.Cli.Commands;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

public class ExplainCommandTests
{
    /// <summary>
    /// A working folder whose global packages folder holds a synthetic nupkg shaped like astroboard 1.1.3 (a legacy
    /// package.manifest only), so explain runs offline from recordings in PR CI (review M4 F2: never live nuget.org).
    /// </summary>
    private static Task<string> WorkingFolderWithAstroboardAsync()
    {
        var gp = TestSites.SharedGlobalPackages();
        var nupkg = NupkgBuilder.Create("astroboard", "1.1.3")
            .WithFile("content/App_Plugins/Astroboard/package.manifest", "{}")
            .WithFile("content/App_Plugins/Astroboard/astroboard.js", "export {};")
            .Build();
        var folder = Path.Combine(gp, "astroboard", "1.1.3");
        Directory.CreateDirectory(folder);
        File.Copy(nupkg, Path.Combine(folder, "astroboard.1.1.3.nupkg"));
        return Task.FromResult(TestSites.Create("17.7.2", gp));
    }

    [Fact]
    public async Task Explain_Astroboard_PrintsEvidence()
    {
        var cwd = await WorkingFolderWithAstroboardAsync();

        var r = await CliHarness.RunInAsync(cwd, TestSites.Handler(), null, null, "explain", "astroboard", "1.1.3", "--target", "17.7.2", "--no-cache");

        Assert.Equal(0, r.Code);
        Assert.Contains("open range >= 13.8.1", r.Console, StringComparison.Ordinal);
        Assert.Contains("legacy package.manifest only", r.Console, StringComparison.Ordinal);
        Assert.Contains("override", r.Console, StringComparison.Ordinal);
        Assert.Contains("computed: suspicious", r.Console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explain_Json_IsOneSiteReport()
    {
        var cwd = await WorkingFolderWithAstroboardAsync();

        var r = await CliHarness.RunInAsync(cwd, TestSites.Handler(), null, null, "explain", "astroboard", "1.1.3", "--target", "17.7.2", "--no-cache", "--format", "json");

        Assert.Equal(0, r.Code);
        using var doc = System.Text.Json.JsonDocument.Parse(r.Stdout);
        var pkg = doc.RootElement.GetProperty("sites")[0].GetProperty("packages")[0];
        Assert.Equal("astroboard", pkg.GetProperty("id").GetString());
        Assert.Equal("suspicious", pkg.GetProperty("override").GetProperty("computedVerdict").GetString());
    }

    [Theory]
    [InlineData("current")]
    [InlineData("next-major")]
    public async Task Explain_CurrentTarget_Exit2(string target)
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), "explain", "astroboard", "1.1.3", "--target", target);

        Assert.Equal(2, r.Code);
        Assert.Contains("current", r.Stderr, StringComparison.Ordinal);
        Assert.Contains("next-major", r.Stderr, StringComparison.Ordinal);
        Assert.Contains("site", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explain_OfflineColdCache_Exit2_NotIncomplete()
    {
        // Review M4 F5: exit 3 is for a failed source; an offline miss is a plain message.
        var r = await CliHarness.RunAsync(TestSites.Handler(), "explain", "astroboard", "--target", "17", "--offline");

        Assert.Equal(2, r.Code);
        Assert.Contains("offline", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explain_TargetRequired_Exit2()
    {
        var r = await CliHarness.RunAsync(TestSites.Handler(), "explain", "astroboard");

        Assert.Equal(2, r.Code);
    }

    [Fact]
    public void Explain_DefaultsToLatestStable() =>
        Assert.Equal(NuGetVersion.Parse("7.2.1"),
            ExplainCommand.LatestStable(new[] { "6.2.1", "6.3.3", "7.2.1", "8.0.0-beta" }.Select(NuGetVersion.Parse).ToList(), allowPrerelease: false));

    [Fact]
    public void Explain_PrereleaseOnly_UsesLatestWhenAllowed()
    {
        var versions = new[] { "1.0.0-alpha", "1.0.0-beta" }.Select(NuGetVersion.Parse).ToList();

        Assert.Null(ExplainCommand.LatestStable(versions, allowPrerelease: false));
        Assert.Equal(NuGetVersion.Parse("1.0.0-beta"), ExplainCommand.LatestStable(versions, allowPrerelease: true));
    }
}
