using PackageCheck.Core;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

public class PlaceholderTests
{
    [Fact]
    public async Task Placeholder_PrintsComingSoon_AndExits2()
    {
        var (code, stdout) = await CliHarness.RunAsync("scan", ".");
        Assert.Equal(2, code);
        Assert.Contains("Umbraco Package Check: coming soon, see https://github.com/PascalA94/Umbraco.Community.PackageCheck", stdout);
    }

    [Fact]
    public void ToolInfo_Version_HasNoSourceLinkSuffix()
    {
        Assert.DoesNotContain('+', ToolInfo.Version);
        Assert.False(string.IsNullOrWhiteSpace(ToolInfo.Version));
    }
}
