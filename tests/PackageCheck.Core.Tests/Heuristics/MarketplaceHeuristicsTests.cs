using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Heuristics;
using static PackageCheck.Core.Tests.Heuristics.HeuristicFacts;
using static PackageCheck.Core.Tests.TestTargets;

namespace PackageCheck.Core.Tests.Heuristics;

internal static class Listing
{
    public static MarketplaceInfo Of(string id, bool retired = false, string? alternate = null, bool hq = false,
        string? latest = null, string? idForTarget = null) =>
        new($"https://marketplace.umbraco.com/package/{id.ToLowerInvariant()}", retired, alternate, hq,
            latest is null ? null : NuGetVersion.Parse(latest), idForTarget ?? id);

    public static PackageFacts With(this PackageFacts f, MarketplaceInfo? m) => f with { Marketplace = m };
}

public class H12Tests
{
    [Fact]
    public void H12_IsInfoOnly()
    {
        var f = Pkg("Umbraco.Community.Contentment", "6.2.1", "[17.0.0, 18.0.0)").With(Listing.Of("Umbraco.Community.Contentment", latest: "7.2.2"));
        var ev = new H12MarketplaceLatest().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal(("H12", EvidenceResult.Info, EvidenceWeight.Info, "marketplace"), (ev.Check, ev.Result, ev.Weight, ev.Source));
        Assert.Equal("Marketplace says 7.2.2 supports 17.7.2", ev.Detail);
    }

    [Fact]
    public void H12_NoListingOrNoLatest_EmitsNothing()
    {
        Assert.Empty(new H12MarketplaceLatest().Evaluate(Pkg("P", "1"), Target("17.7.2")));
        Assert.Empty(new H12MarketplaceLatest().Evaluate(Pkg("P", "1").With(Listing.Of("P")), Target("17.7.2")));
    }
}

public class H13Tests
{
    [Fact]
    public void H13_Retired_IsHighNegativeWithAlternate()
    {
        var f = Pkg("Enterspeed.Source.UmbracoCms.V10", "3.2.0")
            .With(Listing.Of("Enterspeed.Source.UmbracoCms.V10", retired: true, alternate: "Enterspeed.Source.UmbracoCms"));
        var ev = new H13MarketplaceRetired().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal(("H13", EvidenceResult.Negative, EvidenceWeight.High), (ev.Check, ev.Result, ev.Weight));
        Assert.Equal("Enterspeed.Source.UmbracoCms", ev.Alternate);
        Assert.Equal("retired on the Marketplace; alternate Enterspeed.Source.UmbracoCms", ev.Detail);
    }

    [Fact]
    public void H13_RetiredWithoutAlternate_IsInfo()
    {
        var ev = new H13MarketplaceRetired().Evaluate(Pkg("P", "1").With(Listing.Of("P", retired: true)), Target("17.7.2")).Single();
        Assert.Equal((EvidenceResult.Info, (string?)null), (ev.Result, ev.Alternate));
    }

    [Fact]
    public void H13_RenamedForTargetMajor_IsHighNegativeWithAlternate()
    {
        // Ruling R30, API leg: the listing names a different package id for the target's major.
        var f = Pkg("Our.Umbraco.Community.Contentment", "4.7.0")
            .With(Listing.Of("Our.Umbraco.Community.Contentment", idForTarget: "Umbraco.Community.Contentment"));
        var ev = new H13MarketplaceRetired().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal((EvidenceResult.Negative, EvidenceWeight.High, "Umbraco.Community.Contentment"), (ev.Result, ev.Weight, ev.Alternate));
        Assert.Equal("renamed on the Marketplace for Umbraco 17: Umbraco.Community.Contentment", ev.Detail);
    }

    [Fact]
    public void H13_SameIdDifferentCase_IsNotARename() =>
        Assert.Empty(new H13MarketplaceRetired().Evaluate(
            Pkg("astroboard", "1").With(Listing.Of("astroboard", idForTarget: "Astroboard")), Target("17.7.2")));

    [Fact]
    public void H13_Listed_EmitsNothing() =>
        Assert.Empty(new H13MarketplaceRetired().Evaluate(Pkg("P", "1").With(Listing.Of("P")), Target("17.7.2")));
}

public class H19Tests
{
    [Fact]
    public void H19_HqWithStrictRange_RaisesH01ToHigh()
    {
        var f = Pkg("Umbraco.Workflow", "17.5.4", "[17.5.4, 18.0.0)").With(Listing.Of("Umbraco.Workflow", hq: true));
        var ev = new H19HqStrictRange().Evaluate(f, Target("17.7.2")).Single();
        Assert.Equal(("H19", EvidenceResult.Positive, EvidenceWeight.High, "marketplace"), (ev.Check, ev.Result, ev.Weight, ev.Source));
        Assert.Equal("HQ package with a bounded range that allows the target: H01 raised to high", ev.Detail);
    }

    [Theory]
    [InlineData(false, "[17.0.0, 18.0.0)")] // not HQ
    [InlineData(true, "17.0.0")]            // HQ, open range
    [InlineData(true, "[16.0.0, 17.0.0)")]  // HQ, range excludes
    public void H19_OtherCases_EmitNothing(bool hq, string range) =>
        Assert.Empty(new H19HqStrictRange().Evaluate(Pkg("P", "1", range).With(Listing.Of("P", hq: hq)), Target("17.7.2")));

    [Fact]
    public void H19_NoListing_EmitsNothing() =>
        Assert.Empty(new H19HqStrictRange().Evaluate(Pkg("P", "1", "[17.0.0, 18.0.0)"), Target("17.7.2")));
}
