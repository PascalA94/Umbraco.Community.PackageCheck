using NuGet.Versioning;
using PackageCheck.Core.Heuristics;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Overrides;

public class OverrideApplierTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    internal static OverrideEntry Entry(string id = "astroboard", OverrideStatus status = OverrideStatus.Broken, string versions = "[1.0.0, )",
        string umbraco = "[14.0.0, )", OverrideOrigin origin = OverrideOrigin.Community, string? replacement = null,
        DateOnly? reviewBy = null, string summary = "Legacy AngularJS package.manifest only") =>
        new(id, VersionRange.Parse(versions), VersionRange.Parse(umbraco), status, summary,
            new Uri("https://forum.umbraco.com/t/packages-supporting-u17-when-they-do-not/7933/8"), "PascalA94",
            new DateOnly(2026, 11, 5), reviewBy ?? new DateOnly(2027, 11, 5), replacement, origin);

    private static OverrideSet Set(params OverrideEntry[] e) => new(e, "test");

    private static PackageVerdict Computed(Verdict v, string id = "astroboard", string version = "1.1.3", UnknownReason? reason = null) =>
        new(new SitePackage(new PackageRef(id, version, NuGetVersion.Parse(version), true, null), ["Site"]),
            v, v is Verdict.Unknown or Verdict.NotUmbraco ? null : Confidence.Medium, reason, null, null, null,
            [new Evidence("H03", EvidenceResult.Negative, EvidenceWeight.Low, "nuget", "open range >= 13.8.1")], null, null);

    private static PackageVerdict Apply(PackageVerdict c, OverrideSet s, string target = "17.7.2", bool clears = false, DateOnly? today = null) =>
        OverrideApplier.Apply(c, s, Target(target), today ?? Today, clears);

    [Fact]
    public void Match_ReplacesVerdict_KeepsComputed_AndLabels()
    {
        var v = Apply(Computed(Verdict.Suspicious), Set(Entry()));
        Assert.Equal((Verdict.NoRelease, (Confidence?)Confidence.High), (v.Verdict, v.Confidence));
        Assert.Equal((Verdict.Suspicious, "override", false), (v.Override!.ComputedVerdict, v.Override.Label, v.Override.Stale));
        Assert.Contains(v.Evidence, e => e is { Check: "H16", Source: "override" });
        Assert.Contains(v.Evidence, e => e.Check == "H03"); // computed evidence is kept
    }

    [Fact]
    public void Match_BeatsConflictingEvidenceUnknown()
    {
        var v = Apply(Computed(Verdict.Unknown, reason: UnknownReason.ConflictingEvidence), Set(Entry(status: OverrideStatus.Works)));
        Assert.Equal((Verdict.Ok, (UnknownReason?)null), (v.Verdict, v.Reason));
    }

    [Theory]
    [InlineData(UnknownReason.Offline)]
    [InlineData(UnknownReason.SourceError)]
    [InlineData(UnknownReason.UnsafeArchive)]
    [InlineData(UnknownReason.UnresolvedProperty)]
    public void SourceUnknowns_AreNeverOverridden(UnknownReason reason)
    {
        // "When offline or a source fails, the verdict is unknown with a reason, never ok" (global constraints).
        var c = Computed(Verdict.Unknown, reason: reason);
        Assert.Same(c, Apply(c, Set(Entry(status: OverrideStatus.Works))));
    }

    [Fact]
    public void PastReviewBy_StillApplies_MarkedStale()
    {
        var v = Apply(Computed(Verdict.Suspicious), Set(Entry(reviewBy: new DateOnly(2026, 1, 1))));
        Assert.Equal(Verdict.NoRelease, v.Verdict);
        Assert.True(v.Override!.Stale);
    }

    [Fact]
    public void LocalBeatsCommunity()
    {
        var v = Apply(Computed(Verdict.Suspicious), Set(Entry(origin: OverrideOrigin.Local, status: OverrideStatus.Works), Entry()));
        Assert.Equal((Verdict.Ok, "local override"), (v.Verdict, v.Override!.Label));
    }

    [Fact]
    public void IdMatch_IsCaseInsensitive() =>
        Assert.NotNull(Apply(Computed(Verdict.Suspicious, id: "AstroBoard"), Set(Entry())).Override);

    [Fact]
    public void NoMatch_OutsideUmbracoRange()
    {
        var c = Computed(Verdict.Ok);
        Assert.Same(c, Apply(c, Set(Entry()), target: "13.9.0"));
    }

    [Fact]
    public void Match_UsesTheReleaseBaseOfAPrereleaseTarget() =>
        Assert.NotNull(Apply(Computed(Verdict.Ok), Set(Entry(umbraco: "[18.0.0, )")), target: "18.0.0-rc2").Override);

    [Fact]
    public void Works_IsOkHigh_KeepsComputed()
    {
        var v = Apply(Computed(Verdict.Suspicious), Set(Entry(status: OverrideStatus.Works)));
        Assert.Equal((Verdict.Ok, (Confidence?)Confidence.High, Verdict.Suspicious), (v.Verdict, v.Confidence, v.Override!.ComputedVerdict));
    }

    [Fact]
    public void Partial_IsSuspiciousWithSummary()
    {
        var v = Apply(Computed(Verdict.Ok), Set(Entry(status: OverrideStatus.Partial, summary: "Dashboard works; picker does not")));
        Assert.Equal(Verdict.Suspicious, v.Verdict);
        Assert.Equal("Dashboard works; picker does not", v.Evidence.Single(e => e.Check == "H16").Detail);
    }

    [Fact]
    public void Broken_AnotherVersionClears_IsUpgrade() =>
        Assert.Equal(Verdict.Upgrade, Apply(Computed(Verdict.Suspicious), Set(Entry()), clears: true).Verdict);

    [Fact]
    public void Broken_NothingClears_IsNoRelease() =>
        Assert.Equal(Verdict.NoRelease, Apply(Computed(Verdict.Suspicious), Set(Entry()), clears: false).Verdict);

    [Fact]
    public void NoMatch_PackageVersionsExcludeCurrent()
    {
        var c = Computed(Verdict.Suspicious);
        var v = Apply(c, Set(Entry(versions: "[2.0.0, )")));
        Assert.Same(c, v);
        Assert.Null(v.Override);
    }

    [Fact]
    public void Replacement_IsCarriedOnOverride()
    {
        var v = Apply(Computed(Verdict.Ok), Set(Entry(status: OverrideStatus.Partial, replacement: "New.Package")));
        Assert.Equal(("New.Package", Verdict.Suspicious), (v.Override!.Entry.Replacement, v.Verdict));
    }

    [Fact]
    public void Broken_WithReplacement_IsReplacedNamingIt() // ruling R30: the seed carries renames the API cannot show
    {
        var v = Apply(Computed(Verdict.NoRelease, id: "Our.Umbraco.Community.Contentment", version: "4.7.0"),
            Set(Entry(id: "Our.Umbraco.Community.Contentment", replacement: "Umbraco.Community.Contentment")));
        Assert.Equal((Verdict.Replaced, (Confidence?)Confidence.High, "Umbraco.Community.Contentment"), (v.Verdict, v.Confidence, v.Recommended));
        Assert.Equal(Verdict.NoRelease, v.Override!.ComputedVerdict);
    }

    [Fact]
    public void BrokenEntry_BlocksMatchingCandidate() =>
        Assert.True(OverrideApplier.BlocksCandidate(Set(Entry()), "astroboard", NuGetVersion.Parse("1.2.0"), Target("17.7.2")));

    [Fact]
    public void PartialEntry_BlocksMatchingCandidate() =>
        Assert.True(OverrideApplier.BlocksCandidate(Set(Entry(status: OverrideStatus.Partial)), "ASTROBOARD", NuGetVersion.Parse("1.2.0"), Target("17.7.2")));

    [Fact]
    public void WorksEntry_NeverBlocksOrClearsCandidate() =>
        Assert.False(OverrideApplier.BlocksCandidate(Set(Entry(status: OverrideStatus.Works)), "astroboard", NuGetVersion.Parse("1.2.0"), Target("17.7.2")));

    [Fact]
    public void BrokenEntry_OutsideItsRanges_DoesNotBlock()
    {
        Assert.False(OverrideApplier.BlocksCandidate(Set(Entry(versions: "[1.0.0, 1.2.0)")), "astroboard", NuGetVersion.Parse("1.2.0"), Target("17.7.2")));
        Assert.False(OverrideApplier.BlocksCandidate(Set(Entry()), "astroboard", NuGetVersion.Parse("1.2.0"), Target("13.9.0")));
    }
}
