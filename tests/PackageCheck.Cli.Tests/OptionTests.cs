using PackageCheck.Cli.Commands;
using PackageCheck.Core;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

public class OptionTests
{
    [Theory]
    [InlineData("30m", 30)]
    [InlineData("6h", 360)]
    [InlineData("1d", 1440)]
    public void MaxAge_Parses(string text, int minutes) => Assert.Equal(TimeSpan.FromMinutes(minutes), DurationParser.Parse(text));

    [Theory]
    [InlineData("6x")]
    [InlineData("")]
    [InlineData("-1h")]
    public async Task MaxAge_Invalid_Exit2(string text)
    {
        Assert.Throws<UsageException>(() => DurationParser.Parse(text));

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.Create("17.7.2"), "--max-age", text);

        Assert.Equal(2, r.Code);
        Assert.Contains("--max-age", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void PathSet_TrimsAndDeduplicates()
    {
        var dir = Directory.CreateTempSubdirectory("pc-pathset-").FullName;
        var other = Directory.CreateTempSubdirectory("pc-pathset-").FullName;

        var set = PathSet.Normalise([dir, other, dir + Path.DirectorySeparatorChar, Path.Combine(dir, ".")]);

        Assert.Equal([Path.GetFullPath(dir), Path.GetFullPath(other)], set);
    }

    [Fact]
    public void PrivateIds_LoadsIdsAndSkipsComments()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, "# comment\n\n  Our.Private  \nother.one # trailing\n");

        var ids = PrivateIds.Load(file);

        Assert.True(ids.Contains("our.private"));
        Assert.True(ids.Contains("Other.One"));
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task Version_PrintsPlainSemVer()
    {
        var r = await CliHarness.RunAsync(null, "--version");

        Assert.Equal(0, r.Code);
        Assert.Equal(ToolInfo.Version, r.Stdout.Trim());
        Assert.DoesNotContain("+", r.Stdout, StringComparison.Ordinal);
    }
}
