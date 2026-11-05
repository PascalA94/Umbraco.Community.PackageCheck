using PackageCheck.Cli.Commands;
using PackageCheck.Core;
using PackageCheck.Core.Model;
using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

public class FailOnPolicyTests
{
    [Theory]
    [InlineData("none", FailOn.None)]
    [InlineData("suspicious", FailOn.Suspicious)]
    [InlineData("upgrade", FailOn.Upgrade)]
    [InlineData("no-release", FailOn.NoRelease)]
    public void FailOn_Parse(string text, FailOn expected) => Assert.Equal(expected, FailOnPolicy.Parse(text));

    [Theory]
    [InlineData("broken")]
    [InlineData("Suspicious")]
    [InlineData("")]
    public async Task FailOn_Invalid_Exit2(string text)
    {
        Assert.Throws<UsageException>(() => FailOnPolicy.Parse(text));

        var r = await CliHarness.RunAsync(TestSites.Handler(), "scan", TestSites.Create("17.7.2"), "--fail-on", text);

        Assert.Equal(2, r.Code);
        foreach (var value in new[] { "none", "suspicious", "upgrade", "no-release" })
            Assert.Contains(value, r.Stderr, StringComparison.Ordinal);
    }

    public static TheoryData<Verdict, FailOn, bool> VerdictByThreshold()
    {
        var fails = new Dictionary<FailOn, Verdict[]>
        {
            [FailOn.None] = [],
            [FailOn.Suspicious] = [Verdict.Suspicious, Verdict.Upgrade, Verdict.Replaced, Verdict.NoRelease],
            [FailOn.Upgrade] = [Verdict.Upgrade, Verdict.Replaced, Verdict.NoRelease],
            [FailOn.NoRelease] = [Verdict.NoRelease],
        };
        var data = new TheoryData<Verdict, FailOn, bool>();
        foreach (var v in Enum.GetValues<Verdict>())
            foreach (var (t, list) in fails)
                data.Add(v, t, list.Contains(v));
        return data;
    }

    [Theory]
    [MemberData(nameof(VerdictByThreshold))]
    public void Exceeds_Matrix(Verdict v, FailOn t, bool expected) => Assert.Equal(expected, FailOnPolicy.Exceeds(v, t));

    [Fact]
    public void Matrix_Has28Rows() => Assert.Equal(28, VerdictByThreshold().Count);
}
