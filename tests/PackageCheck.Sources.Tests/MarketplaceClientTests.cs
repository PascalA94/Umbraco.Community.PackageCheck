using System.Net;
using NuGet.Versioning;
using PackageCheck.Sources.Marketplace;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

public class MarketplaceClientTests
{
    private const string Api = "https://api.marketplace.umbraco.com/api/v1.0/packages/";

    // Trimmed from the 2026-11-03 recordings (spikes/M0-golden/recorded): free-text README fields left out (ruling R33).
    private const string Enterspeed = """
        { "packageId": "Enterspeed.Source.UmbracoCms.V10", "isHQ": false, "isRetired": true,
          "retiredReason": "This package is no longer maintained.",
          "alternatePackageIdForRetiredPackage": "Enterspeed.Source.UmbracoCms",
          "latestVersionNumberForProvidedUmbracoVersion": "3.2.0",
          "packageIdForProvidedUmbracoVersion": "Enterspeed.Source.UmbracoCms.V10", "versionSpecificPackageIds": [] }
        """;

    private const string Workflow = """
        { "packageId": "Umbraco.Workflow", "isHQ": true, "isRetired": false, "retiredReason": null,
          "alternatePackageIdForRetiredPackage": null, "latestVersionNumberForProvidedUmbracoVersion": "17.5.4",
          "packageIdForProvidedUmbracoVersion": "Umbraco.Workflow", "versionSpecificPackageIds": [] }
        """;

    private static readonly NuGetVersion Target = NuGetVersion.Parse("17.7.2");

    private static (MarketplaceClient Client, FixtureHttpHandler Handler, List<string> Warnings) Client()
    {
        var handler = new FixtureHttpHandler(Path.Combine(Path.GetTempPath(), "pc-no-fixtures"), RecordMode.Replay);
        var warnings = new List<string>();
        return (new MarketplaceClient(new HttpClient(handler), warnings.Add), handler, warnings);
    }

    [Fact]
    public async Task Detail_ParsesRetiredAlternateAndHq()
    {
        var (client, handler, _) = Client();
        handler.Route(Api + "Enterspeed.Source.UmbracoCms.V10", HttpStatusCode.OK, Enterspeed)
               .Route(Api + "Umbraco.Workflow", HttpStatusCode.OK, Workflow);

        var e = await client.GetAsync("Enterspeed.Source.UmbracoCms.V10", Target, CancellationToken.None);
        Assert.NotNull(e);
        Assert.True(e.IsRetired);
        Assert.Equal("Enterspeed.Source.UmbracoCms", e.AlternatePackageId);
        Assert.False(e.IsHq);
        Assert.Equal(NuGetVersion.Parse("3.2.0"), e.LatestForTarget);
        Assert.Equal("Enterspeed.Source.UmbracoCms.V10", e.PackageIdForTarget);
        Assert.Equal("https://marketplace.umbraco.com/package/enterspeed.source.umbracocms.v10", e.Url);

        var w = await client.GetAsync("Umbraco.Workflow", Target, CancellationToken.None);
        Assert.True(w!.IsHq);
        Assert.False(w.IsRetired);
        Assert.Null(w.AlternatePackageId);
    }

    [Fact]
    public void PackageDetailUrl_EscapesIdAndTarget() =>
        Assert.Equal(Api + "a%2Fb%3Fc?SupportsUmbracoVersionNumber=18.0.0-rc2",
            MarketplaceUrls.PackageDetail("a/b?c", NuGetVersion.Parse("18.0.0-rc2")).AbsoluteUri);

    [Fact]
    public async Task NotListed_ReturnsNull_WithoutWarning()
    {
        var (client, handler, warnings) = Client();
        handler.Route(Api + "Plumber.Workflow", HttpStatusCode.NotFound, """{"status":404}""");
        Assert.Null(await client.GetAsync("Plumber.Workflow", Target, CancellationToken.None));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("500")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    [InlineData("wrong-shape")]
    [InlineData("refused")]
    public async Task AnyError_ReturnsNull_NoThrow(string kind)
    {
        var (client, handler, warnings) = Client();
        handler.Route(Api, kind switch
        {
            "500" => (_, _) => Task.FromResult(FixtureHttpHandler.Json(HttpStatusCode.InternalServerError, "{}")),
            "timeout" => (_, _) => throw new TaskCanceledException("timed out", new TimeoutException()),
            "malformed" => (_, _) => Task.FromResult(FixtureHttpHandler.Json(HttpStatusCode.OK, "{ not json")),
            "wrong-shape" => (_, _) => Task.FromResult(FixtureHttpHandler.Json(HttpStatusCode.OK, """{"isRetired":"yes","latestVersionNumberForProvidedUmbracoVersion":42}""")),
            _ => (_, _) => throw new HttpRequestException("refused", new System.Net.Sockets.SocketException(61)),
        });
        Assert.Null(await client.GetAsync("Any.Package", Target, CancellationToken.None));
        Assert.Single(warnings);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var (client, handler, _) = Client();
        handler.Route(Api, (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(FixtureHttpHandler.Json(HttpStatusCode.OK, Workflow)); });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("Umbraco.Workflow", Target, cts.Token));
    }

    [Fact]
    public async Task NeverCallsValidatePackage()
    {
        var (client, handler, _) = Client();
        handler.Route(Api, HttpStatusCode.OK, Workflow);
        foreach (var id in new[] { "A", "B.C", "Umbraco.Workflow", "astroboard", "Crumpled.RobotsTxt" })
            await client.GetAsync(id, Target, CancellationToken.None);
        Assert.Equal(5, handler.RequestCount);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("validatePackage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceHasNoValidatePackageString()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UmbracoPackageCheck.slnx"))) dir = dir.Parent;
        var src = Path.Combine(dir!.FullName, "src");
        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("validatePackage", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(hits);
    }
}

public class MarketplaceClientBodyFailureTests
{
    private sealed class ThrowingStream : MemoryStream
    {
        public ThrowingStream() : base("{\"isHQ\":"u8.ToArray()) { }
        private int _reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _reads++ == 0 ? base.ReadAsync(buffer, ct) : throw new IOException("connection reset mid-body");
        public override int Read(byte[] buffer, int offset, int count) =>
            _reads++ == 0 ? base.Read(buffer, offset, count) : throw new IOException("connection reset mid-body");
    }

    [Theory]
    [InlineData("io")]
    [InlineData("gzip")]
    public async Task BodyFailure_ReturnsNull_WithAWarning(string kind) // M2 review I4
    {
        var handler = new PackageCheck.Tests.Support.FixtureHttpHandler(Path.Combine(Path.GetTempPath(), "pc-no-fixtures"), PackageCheck.Tests.Support.RecordMode.Replay);
        handler.Route("https://api.marketplace.umbraco.com/", (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = kind == "io" ? new StreamContent(new ThrowingStream()) : new StreamContent(new InvalidDataStream()),
        }));
        var warnings = new List<string>();
        var client = new PackageCheck.Sources.Marketplace.MarketplaceClient(new HttpClient(handler), warnings.Add);
        Assert.Null(await client.GetAsync("P", NuGetVersion.Parse("17.7.2"), TestContext.Current.CancellationToken));
        Assert.Single(warnings);
    }

    private sealed class InvalidDataStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw new InvalidDataException("corrupt gzip");
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidDataException("corrupt gzip");
    }
}
