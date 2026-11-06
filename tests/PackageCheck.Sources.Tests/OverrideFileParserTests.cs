using NuGet.Versioning;
using PackageCheck.Core;
using PackageCheck.Core.Overrides;
using PackageCheck.Sources.Data;
using PackageCheck.Sources.Overrides;

namespace PackageCheck.Sources.Tests;

public class OverrideFileParserTests
{
    private const string Yaml = """
        id: Astroboard
        entries:
          - packageVersions: "[1.0.0, )"
            umbraco: "[14.0.0, )"
            status: broken
            summary: "Legacy AngularJS package.manifest only; does not load in the new backoffice"
            evidence: https://forum.umbraco.com/t/packages-supporting-u17-when-they-do-not/7933/8
            reportedBy: "PascalA94"
            verifiedOn: 2026-11-05
            replacement: null
        """;

    private const string Json = """
        { "id": "Astroboard", "entries": [ { "packageVersions": "[1.0.0, )", "umbraco": "[14.0.0, )", "status": "broken",
          "summary": "Legacy AngularJS package.manifest only; does not load in the new backoffice",
          "evidence": "https://forum.umbraco.com/t/packages-supporting-u17-when-they-do-not/7933/8",
          "reportedBy": "PascalA94", "verifiedOn": "2026-11-05", "replacement": null } ] }
        """;

    [Fact]
    public void Parse_Yaml_And_Json_Equivalent()
    {
        var y = OverrideFileParser.Parse(Yaml, "astroboard.yml", OverrideOrigin.Local);
        var j = OverrideFileParser.Parse(Json, "astroboard.json", OverrideOrigin.Local);
        var e = Assert.Single(y.Entries);
        Assert.Equal(e, Assert.Single(j.Entries));
        Assert.Equal(("Astroboard", OverrideStatus.Broken, OverrideOrigin.Local), (e.Id, e.Status, e.Origin));
        Assert.Equal(VersionRange.Parse("[14.0.0, )"), e.Umbraco);
        Assert.Null(e.Replacement);
    }

    [Fact]
    public void ReviewBy_DefaultsToTwelveMonthsAfterVerifiedOn() =>
        Assert.Equal(new DateOnly(2027, 11, 5), Assert.Single(OverrideFileParser.Parse(Yaml, "a.yml", OverrideOrigin.Local).Entries).ReviewBy);

    [Theory]
    [InlineData("http://example.com/issue/1")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://github.com@evil.example/x")] // security review L4: userinfo makes a link look like another host
    [InlineData("https://user:pass@example.com/x")]
    [InlineData("")]
    public void Parse_NonHttpsEvidence_Rejected(string evidence)
    {
        var warnings = new List<string>();
        var set = OverrideFileParser.Parse(Yaml.Replace("https://forum.umbraco.com/t/packages-supporting-u17-when-they-do-not/7933/8", $"\"{evidence}\""),
            "a.yml", OverrideOrigin.Local, warnings);
        Assert.Empty(set.Entries);
        Assert.Contains("evidence", Assert.Single(warnings));
    }

    [Theory]
    [InlineData("status: broken", "status: sometimes")]
    [InlineData("umbraco: \"[14.0.0, )\"", "umbraco: \"not a range\"")]
    [InlineData("verifiedOn: 2026-11-05", "verifiedOn: yesterday")]
    public void Parse_InvalidEntry_IsRejectedWithAWarning(string from, string to)
    {
        var warnings = new List<string>();
        Assert.Empty(OverrideFileParser.Parse(Yaml.Replace(from, to), "a.yml", OverrideOrigin.Local, warnings).Entries);
        Assert.Single(warnings);
    }

    [Fact]
    public void Parse_Malformed_IsUsageError() =>
        Assert.Throws<UsageException>(() => OverrideFileParser.Parse("id: [unclosed", "bad.yml", OverrideOrigin.Local));

    [Fact]
    public async Task Parse_SeedShape_ListOfPackages()
    {
        var set = await new BundledOverrideSource().LoadAsync(TestContext.Current.CancellationToken);
        Assert.Contains(set.Entries, e => e.Id == "astroboard" && e.Status == OverrideStatus.Broken && e.Origin == OverrideOrigin.Community);
        Assert.Contains(set.Entries, e => e.Id == "Our.Umbraco.Community.Contentment" && e.Replacement == "Umbraco.Community.Contentment");
        Assert.All(set.Entries, e => Assert.Equal("https", e.Evidence.Scheme));
    }

    [Fact]
    public async Task Composite_LaterSourceWins_ForSameIdAndRanges()
    {
        var community = new FixedSource(OverrideFileParser.Parse(Yaml, "a.yml", OverrideOrigin.Community));
        var local = new FixedSource(OverrideFileParser.Parse(Yaml.Replace("status: broken", "status: works"), "b.yml", OverrideOrigin.Local));
        var merged = await new CompositeOverrideSource([community, local]).LoadAsync(TestContext.Current.CancellationToken);
        var e = Assert.Single(merged.Entries);
        Assert.Equal((OverrideStatus.Works, OverrideOrigin.Local), (e.Status, e.Origin));
    }

    private sealed class FixedSource(OverrideSet set) : IOverrideSource
    {
        public Task<OverrideSet> LoadAsync(CancellationToken ct) => Task.FromResult(set);
    }
}

public class OverrideFileParserHostileInputTests
{
    [Fact]
    public void DeeplyNestedOverrides_AreIgnored_NotACrash() // M2 review I6
    {
        const int depth = 5_000;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < depth; i++) sb.Append("{\"overrides\": [");
        for (var i = 0; i < depth; i++) sb.Append("]}");
        Assert.Empty(OverrideFileParser.Parse(sb.ToString(), "deep.json", OverrideOrigin.Local, []).Entries);
    }

    [Fact]
    public void OversizeContent_IsUsageError() =>
        Assert.Throws<PackageCheck.Core.UsageException>(() =>
            OverrideFileParser.Parse(new string(' ', OverrideFileParser.MaxBytes + 1), "big.yml", OverrideOrigin.Local));

    [Fact]
    public void UnknownTag_IsRejectedNotInstantiated() =>
        Assert.ThrowsAny<Exception>(() => OverrideFileParser.Parse("id: !System.Diagnostics.Process x\nentries: []", "tag.yml", OverrideOrigin.Local));
}
