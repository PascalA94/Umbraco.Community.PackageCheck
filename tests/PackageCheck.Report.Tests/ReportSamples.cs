using Microsoft.Extensions.Time.Testing;
using NuGet.Frameworks;
using NuGet.Versioning;
using PackageCheck.Core;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Report;
using PackageCheck.Report.Model;

namespace PackageCheck.Report.Tests;

/// <summary>Builder-made reports for the writer tests (preflight row 25: Report.Tests cannot run the engine).</summary>
internal static class ReportSamples
{
    public static readonly DateTimeOffset Now = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    public static ReportOptions Options(FailOn failOn = FailOn.None) => new(failOn, Offline: false, Transitive: false, Inspect: true, Prerelease: false);

    public static TargetVersion Target(string version = "17.7.2", string requested = "current", string tfm = "net10.0")
    {
        var v = NuGetVersion.Parse(version);
        return new TargetVersion(requested, v, NuGetFramework.Parse(tfm), v.IsPrerelease);
    }

    public static PackageVerdict Verdict(
        string id,
        Verdict verdict,
        Confidence? confidence = Confidence.High,
        UnknownReason? reason = null,
        string version = "1.0.0",
        IReadOnlyList<Evidence>? evidence = null,
        MarketplaceInfo? marketplace = null,
        OverrideApplied? overrideApplied = null,
        string? recommended = null,
        string? latestStable = null,
        bool latestStableClears = false) =>
        new(
            new SitePackage(new PackageRef(id, version, NuGetVersion.Parse(version), Direct: true, UnresolvedReason: null), ["Web"]),
            verdict,
            verdict is Core.Model.Verdict.Unknown or Core.Model.Verdict.NotUmbraco ? null : confidence,
            reason,
            recommended,
            HighestCompatible: null,
            PrereleaseCompatible: null,
            evidence ?? [new Evidence("H01", EvidenceResult.Positive, EvidenceWeight.Medium, EvidenceSources.NuGet, "range allows 17.7.2")],
            marketplace,
            overrideApplied,
            latestStable,
            latestStableClears);

    public static ScanInput Input(string sitePath = "/sites/MySite", params string[] warnings) =>
        new(
            sitePath,
            [new ProjectInfo(Path.Combine(sitePath, "src", "Web", "Web.csproj"), ["net10.0"], NuGetVersion.Parse("17.7.2"), [])],
            warnings,
            TransitiveStatus.NotRequested);

    public static SiteResult Site(params PackageVerdict[] verdicts) => new(Input(), Target(), verdicts, Incomplete: false);

    public static ScanReport Build(IReadOnlyList<SiteResult> sites, IReadOnlyList<string>? runWarnings = null, ReportOptions? options = null) =>
        ReportBuilder.Build(sites, runWarnings ?? [], options ?? Options(), new FakeTimeProvider(Now));

    public static OverrideApplied Override(Verdict computed, OverrideStatus status = OverrideStatus.Broken, string summary = "Legacy manifest only",
        OverrideOrigin origin = OverrideOrigin.Community, bool stale = false, string? replacement = null) =>
        new(
            new OverrideEntry("Astroboard", VersionRange.Parse("[1.0.0, )"), VersionRange.Parse("[14.0.0, )"), status, summary,
                new Uri("https://github.com/example/compat/issues/1"), "someone", new DateOnly(2026, 10, 29), new DateOnly(2027, 10, 29),
                replacement, origin),
            computed,
            stale,
            origin == OverrideOrigin.Local ? "local override" : "override");

    /// <summary>One report that exercises every verdict, confidence, reason, evidence result and weight wire value.</summary>
    public static ScanReport EveryValue()
    {
        var verdicts = new List<PackageVerdict>
        {
            Verdict("A.Ok", Core.Model.Verdict.Ok, Confidence.High),
            Verdict("A.Upgrade", Core.Model.Verdict.Upgrade, Confidence.Medium, recommended: "2.0.0", latestStable: "3.0.0"),
            Verdict("A.NoRelease", Core.Model.Verdict.NoRelease, Confidence.High),
            Verdict("A.Suspicious", Core.Model.Verdict.Suspicious, Confidence.Low,
                evidence:
                [
                    new Evidence("H03", EvidenceResult.Negative, EvidenceWeight.Low, EvidenceSources.NuGet, "open range"),
                    new Evidence("H11", EvidenceResult.Info, EvidenceWeight.Info, EvidenceSources.Nupkg, "note"),
                    new Evidence("H08", EvidenceResult.Negative, EvidenceWeight.High, EvidenceSources.Nupkg, "legacy package.manifest only"),
                ]),
            Verdict("A.Replaced", Core.Model.Verdict.Replaced, Confidence.High, recommended: "B.New",
                marketplace: new MarketplaceInfo("https://marketplace.umbraco.com/package/a.replaced", true, "B.New", false, null, null)),
            Verdict("A.NotUmbraco", Core.Model.Verdict.NotUmbraco),
            Verdict("A.Overridden", Core.Model.Verdict.NoRelease, Confidence.High,
                overrideApplied: Override(Core.Model.Verdict.Suspicious, OverrideStatus.Partial, stale: true, replacement: "B.New")),
            Verdict("A.Works", Core.Model.Verdict.Ok, overrideApplied: Override(Core.Model.Verdict.Unknown, OverrideStatus.Works, origin: OverrideOrigin.Local)),
        };
        verdicts.AddRange(Enum.GetValues<UnknownReason>().Select(r => Verdict($"U.{r}", Core.Model.Verdict.Unknown, reason: r)));

        var input = Input() with { Transitive = TransitiveStatus.Unavailable };
        return Build(
            [new SiteResult(input, Target("18.0.0-rc2", "next-major"), verdicts, Incomplete: true),
             new SiteResult(Input("/sites/Other", "project Old.csproj not found; skipped"), null, [], Incomplete: true, RequestedTarget: "17")],
            ["no Umbraco project found in /x; skipped"],
            Options(FailOn.NoRelease));
    }
}
