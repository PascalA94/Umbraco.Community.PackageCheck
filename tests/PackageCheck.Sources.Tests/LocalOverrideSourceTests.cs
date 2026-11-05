using System.Net;
using PackageCheck.Core;
using PackageCheck.Core.Overrides;
using PackageCheck.Sources.Overrides;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

public class LocalOverrideSourceTests
{
    private const string Yaml = """
        id: My.Package
        entries:
          - packageVersions: "[1.0.0, )"
            umbraco: "[17.0.0, )"
            status: works
            summary: "Tested on our sites"
            evidence: https://github.com/example/repo/issues/1
            verifiedOn: 2026-11-01
        """;

    private static (HttpClient, FixtureHttpHandler) Http()
    {
        var h = new FixtureHttpHandler(Path.Combine(Path.GetTempPath(), "pc-no-fixtures"), RecordMode.Replay);
        return (new HttpClient(h), h);
    }

    [Fact]
    public async Task Local_File_IsRead()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("pc-ov-").FullName, "overrides.yml");
        await File.WriteAllTextAsync(path, Yaml, TestContext.Current.CancellationToken);
        var (http, _) = Http();
        var set = await new LocalOverrideSource(path, http).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OverrideOrigin.Local, Assert.Single(set.Entries).Origin);
    }

    [Fact]
    public async Task Local_MissingFile_IsUsageError()
    {
        var (http, _) = Http();
        await Assert.ThrowsAsync<PathNotFoundException>(() =>
            new LocalOverrideSource(Path.Combine(Path.GetTempPath(), "pc-missing", "x.yml"), http).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Local_HttpsUrl_IsFetched()
    {
        var (http, handler) = Http();
        handler.Route("https://example.com/overrides.yml", (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Yaml) }));
        var set = await new LocalOverrideSource("https://example.com/overrides.yml", http).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("My.Package", OverrideOrigin.Local), (Assert.Single(set.Entries).Id, set.Entries[0].Origin));
    }

    [Theory]
    [InlineData("http://example.com/overrides.yml")]
    [InlineData("ftp://example.com/overrides.yml")]
    public void Local_HttpUrl_IsUsageError(string url)
    {
        var (http, _) = Http();
        Assert.Throws<UsageException>(() => new LocalOverrideSource(url, http));
    }

    [Fact]
    public async Task Local_FetchFails_WarnsAndEmpty()
    {
        var (http, handler) = Http();
        handler.Route("https://example.com/", HttpStatusCode.InternalServerError, "{}");
        var warnings = new List<string>();
        var set = await new LocalOverrideSource("https://example.com/overrides.yml", http, warnings.Add).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(set.Entries);
        Assert.Single(warnings);
    }
}

public class LocalOverrideSourceBodyFailureTests
{
    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw new IOException("reset");
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("reset");
    }

    [Fact]
    public async Task Local_BodyFailsMidRead_WarnsAndEmpty() // M2 review I4
    {
        var h = new FixtureHttpHandler(Path.Combine(Path.GetTempPath(), "pc-no-fixtures"), RecordMode.Replay);
        h.Route("https://example.com/", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) }));
        var warnings = new List<string>();
        var set = await new LocalOverrideSource("https://example.com/o.yml", new HttpClient(h), warnings.Add).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(set.Entries);
        Assert.Single(warnings);
    }
}
