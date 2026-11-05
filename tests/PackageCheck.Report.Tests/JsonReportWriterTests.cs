using System.Text;
using System.Text.Json;
using PackageCheck.Core;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Report.Model;
using static PackageCheck.Report.Tests.ReportSamples;

namespace PackageCheck.Report.Tests;

public class JsonReportWriterTests
{
    internal static string Json(ScanReport r)
    {
        using var ms = new MemoryStream();
        JsonReportWriter.Write(r, ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static JsonElement Root(ScanReport r) => JsonDocument.Parse(Json(r)).RootElement.Clone();

    private static JsonElement Package(JsonElement root, string id) =>
        root.GetProperty("sites")[0].GetProperty("packages").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);

    [Fact]
    public void Json_EnumWireValues()
    {
        var root = Root(EveryValue());

        Assert.Equal("no-release", Package(root, "A.NoRelease").GetProperty("verdict").GetString());
        Assert.Equal("not-umbraco", Package(root, "A.NotUmbraco").GetProperty("verdict").GetString());
        var conflicting = Package(root, "U.ConflictingEvidence");
        Assert.Equal("conflicting-evidence", conflicting.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, conflicting.GetProperty("confidence").ValueKind);
        Assert.Equal("low", Package(root, "A.Suspicious").GetProperty("confidence").GetString());
        var h08 = Package(root, "A.Suspicious").GetProperty("evidence")[2];
        Assert.Equal("negative", h08.GetProperty("result").GetString());
        Assert.Equal("high", h08.GetProperty("weight").GetString());
        Assert.Equal("no-release", root.GetProperty("options").GetProperty("failOn").GetString());
        Assert.Equal("2026-12-01T10:00:00Z", root.GetProperty("generatedAt").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(ToolInfo.Name, root.GetProperty("tool").GetProperty("name").GetString());
    }

    [Fact]
    public void Json_UnknownCarriesReason()
    {
        var root = Root(Build([Site(Verdict("X", Core.Model.Verdict.Unknown, reason: UnknownReason.Offline))]));

        var p = Package(root, "X");
        Assert.Equal("unknown", p.GetProperty("verdict").GetString());
        Assert.Equal("offline", p.GetProperty("reason").GetString());
    }

    [Fact]
    public void Json_FeedUrlsAndPathsHaveNoCredentials()
    {
        const string token = "s3cr3tT0ken"; // gitleaks:allow
        var evidence = new Evidence("H01", EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.NuGet,
            $"feed https://user:{token}@pkgs.example.com/v3/index.json returned 500");
        var input = Input("/sites/MySite", $"feed https://u:{token}@pkgs.example.com failed");
        var report = Build(
            [new SiteResult(input, Target(), [Verdict("X", Core.Model.Verdict.Unknown, reason: UnknownReason.SourceError, evidence: [evidence])], true)],
            [$"source https://{token}@feed.example.com unreachable"]);

        var json = Json(report);

        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        Assert.Contains("https://pkgs.example.com/v3/index.json", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_ExcludeNotUmbraco()
    {
        var report = Build([Site(
            Verdict("A", Core.Model.Verdict.Ok),
            Verdict("B", Core.Model.Verdict.NotUmbraco),
            Verdict("C", Core.Model.Verdict.Suspicious, Confidence.Medium))]);

        var counts = Root(report).GetProperty("summary").GetProperty("counts");

        Assert.Equal(["ok", "upgrade", "no-release", "suspicious", "replaced", "unknown"], counts.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, counts.GetProperty("ok").GetInt32());
        Assert.Equal(1, counts.GetProperty("suspicious").GetInt32());
        // not-umbraco packages stay in the JSON; hiding them is a console and markdown concern.
        Assert.Equal(3, Root(report).GetProperty("sites")[0].GetProperty("packages").GetArrayLength());
    }

    [Fact]
    public void Json_Override_ShowsOverrideVerdict_AndComputed()
    {
        var root = Root(Build([Site(Verdict("Astroboard", Core.Model.Verdict.NoRelease,
            overrideApplied: Override(Core.Model.Verdict.Suspicious)))]));

        var p = Package(root, "Astroboard");
        Assert.Equal("no-release", p.GetProperty("verdict").GetString());
        var o = p.GetProperty("override");
        Assert.Equal("override", o.GetProperty("label").GetString());
        Assert.Equal("suspicious", o.GetProperty("computedVerdict").GetString());
        Assert.Equal("broken", o.GetProperty("status").GetString());
        Assert.Equal("https://github.com/example/compat/issues/1", o.GetProperty("evidence").GetString());
        Assert.Equal("2027-10-29", o.GetProperty("reviewBy").GetString());
        Assert.False(o.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Json_PrereleaseTarget_Marked()
    {
        var site = new SiteResult(Input(), Target("18.0.0-rc2", "next-major"), [], Incomplete: false);

        var target = Root(Build([site])).GetProperty("sites")[0].GetProperty("target");

        Assert.True(target.GetProperty("prerelease").GetBoolean());
        Assert.Equal("18.0.0-rc2", target.GetProperty("resolved").GetString());
        Assert.Equal("next-major", target.GetProperty("requested").GetString());
        Assert.Equal("net10.0", target.GetProperty("tfm").GetString());
    }

    [Fact]
    public void Json_RunWarnings_Present()
    {
        var root = Root(Build([Site()], ["no Umbraco project found in /x; skipped"]));

        Assert.Equal("no Umbraco project found in /x; skipped", root.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public void Json_UnresolvedTarget_RepeatsRequested_AndIsIncomplete()
    {
        var site = new SiteResult(Input(), null, [], Incomplete: true, RequestedTarget: "17");

        var root = Root(Build([site]));

        var target = root.GetProperty("sites")[0].GetProperty("target");
        Assert.Equal("17", target.GetProperty("requested").GetString());
        Assert.Equal("17", target.GetProperty("resolved").GetString());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("tfm").ValueKind);
        Assert.True(root.GetProperty("summary").GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public void Json_TransitiveUnavailable_MarksSummary()
    {
        var site = new SiteResult(Input() with { Transitive = TransitiveStatus.Unavailable }, Target(), [], Incomplete: false);

        var summary = Root(Build([site])).GetProperty("summary");

        Assert.Equal("unavailable", summary.GetProperty("transitive").GetString());
        Assert.True(summary.GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public void Json_SourceErrorVerdict_MarksIncomplete()
    {
        var report = Build([Site(Verdict("X", Core.Model.Verdict.Unknown, reason: UnknownReason.SourceError))]);

        Assert.True(report.Summary.Incomplete);
    }

    [Fact]
    public void Json_PackageProjects_ArePaths_AndSameNamedProjectsBothListed()
    {
        // Review M4 F3 / design 7.6: packages[].projects are site-relative paths, joinable to sites[].projects[].path.
        var pkg = new PackageRef("X", "1.0.0", NuGet.Versioning.NuGetVersion.Parse("1.0.0"), true, null);
        var input = new ScanInput("/sites/S",
        [
            new ProjectInfo("/sites/S/a/Web/Web.csproj", ["net10.0"], NuGet.Versioning.NuGetVersion.Parse("17.7.2"), [pkg]),
            new ProjectInfo("/sites/S/b/Web/Web.csproj", ["net10.0"], NuGet.Versioning.NuGetVersion.Parse("17.7.2"), [pkg]),
        ], [], TransitiveStatus.NotRequested);
        var sitePackage = SitePackages.From(input).Single();
        var verdict = Verdict("X", Core.Model.Verdict.Ok) with { Package = sitePackage };

        var p = Package(Root(Build([new SiteResult(input, Target(), [verdict], false)])), "X");

        Assert.Equal(["a/Web/Web.csproj", "b/Web/Web.csproj"], p.GetProperty("projects").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("https://user:pa/ss@feed.example.test/v3/index.json")]
    [InlineData("https://user:pa%2Fss@feed.example.test/v3/index.json")]
    [InlineData("see http://tok3n@feed.example.test now")]
    public void Redaction_StripsAnyUserInfo(string text)
    {
        // Security review M4-S3: userinfo with '/' must not survive the last line of defence.
        var json = Json(Build([Site()], [text]));

        Assert.DoesNotContain("pa/ss", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pa%2Fss", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tok3n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_ProjectPaths_AreRelativeToSite()
    {
        var project = Root(Build([Site()])).GetProperty("sites")[0].GetProperty("projects")[0];

        Assert.Equal("src/Web/Web.csproj", project.GetProperty("path").GetString());
        Assert.Equal("17.7.2", project.GetProperty("umbracoVersion").GetString());
    }
}
