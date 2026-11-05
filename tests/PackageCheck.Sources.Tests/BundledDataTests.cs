using System.Text.Json;
using NuGet.Frameworks;
using PackageCheck.Sources.Data;

namespace PackageCheck.Sources.Tests;

public class BundledDataTests
{
    [Fact]
    public async Task UmbracoVersions_HoldsMajors13To18()
    {
        var table = await new BundledUmbracoVersionsSource().GetTableAsync(CancellationToken.None);
        Assert.Equal(Enumerable.Range(13, 6), table.Majors.Select(m => m.Major));
        Assert.All(table.Majors, m => Assert.NotEqual(NuGetFramework.UnsupportedFramework, NuGetFramework.Parse(m.Tfm)));
    }

    [Fact]
    public async Task UmbracoVersions_LatestLtsOnCheckDate_IsTheHighestSupportedLts()
    {
        var table = await new BundledUmbracoVersionsSource().GetTableAsync(CancellationToken.None);
        var lts = table.Majors.Where(m => m.Lts && m.Eol >= new DateOnly(2026, 11, 5)).Max(m => m.Major);
        Assert.Equal(lts, table.LatestLtsMajor(new DateOnly(2026, 11, 5)));
        Assert.True(table.Majors.Single(m => m.Major == lts).Lts);
    }

    [Fact]
    public void OverridesSeed_IsEmbedded_AndNamesAstroboardWithPublicEvidence()
    {
        using var s = BundledData.Open(BundledData.OverridesSeedResource);
        using var doc = JsonDocument.Parse(s);
        var astroboard = doc.RootElement.GetProperty("overrides").EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == "astroboard");
        var evidence = astroboard.GetProperty("entries")[0].GetProperty("evidence").GetString();
        Assert.StartsWith("https://", evidence);
    }
}
