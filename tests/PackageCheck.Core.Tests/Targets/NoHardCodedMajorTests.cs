using System.Text.RegularExpressions;

namespace PackageCheck.Core.Tests.Targets;

/// <summary>
/// Global constraint: no Umbraco major is hard-coded in src/. Majors come from the feed or umbraco-versions.json.
/// The one allowed literal is <c>UmbracoFacts.NewBackofficeMajor</c> (the v14 backoffice cut-over, design 7.4).
/// </summary>
public partial class NoHardCodedMajorTests
{
    // Comparisons against, or construction from, a 13-19 literal: `Major >= 17`, `17 == x.Major`,
    // `new NuGetVersion(17`, `"17.`, `NewBackofficeMajor = 14`.
    [GeneratedRegex(@"(Major\w*\s*(==|!=|<=|>=|<|>|=)\s*1[3-9]\b)|(\b1[3-9]\s*(==|!=|<=|>=|<|>)\s*\w*\.?Major)|(new\s+(NuGet|Semantic)?Version\(\s*1[3-9]\s*,)|(""1[3-9]\.)|(case\s+1[3-9]\s*:)")]
    private static partial Regex MajorLiteral();

    private const string Allowed = "NewBackofficeMajor = 14";

    [Fact]
    public void NoLiteralUmbracoMajorsInSrc()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("///"))
            .Where(x => MajorLiteral().IsMatch(x.line) && !x.line.Contains(Allowed))
            .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(hits.Count == 0, "Literal Umbraco majors in src/:\n" + string.Join("\n", hits));
    }

    [Theory]
    [InlineData("if (v.Major >= 17) return;")]
    [InlineData("var x = 18 == target.Major;")]
    [InlineData("new NuGetVersion(17, 0, 0)")]
    [InlineData("var s = \"17.7.2\";")]
    public void Regex_CatchesTheShapes(string line) => Assert.Matches(MajorLiteral(), line);

    [Fact]
    public void AllowedConstant_Exists()
    {
        Assert.Equal(14, UmbracoFacts.NewBackofficeMajor);
        var hits = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Count(f => File.ReadAllText(f).Contains(Allowed));
        Assert.Equal(1, hits);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UmbracoPackageCheck.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
