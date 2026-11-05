using System.Net;
using Microsoft.Extensions.Time.Testing;
using PackageCheck.Core;
using PackageCheck.Sources.Http;
using PackageCheck.Tests.Support;

namespace PackageCheck.Sources.Tests;

public class HttpPolicyTests
{
    private const string Url = "https://policy.test/x";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (HttpClient Client, FixtureHttpHandler Inner) Client(HttpPolicy policy)
    {
        var inner = new FixtureHttpHandler(Path.GetTempPath(), RecordMode.Replay);
        return (new HttpClient(policy.CreateHandler(inner)), inner);
    }

    [Fact]
    public async Task UserAgent_ReplacesAnyCallerValue_Exactly()
    {
        var (client, inner) = Client(new HttpPolicy(new FakeTimeProvider()));
        inner.Route(Url, HttpStatusCode.OK, "{}");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NuGet/7.9.0");

        await client.GetAsync(Url, Ct);

        Assert.Equal(ToolInfo.UserAgent, Assert.Single(inner.UserAgents));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Retryable_IsTriedExactlyMaxTries(HttpStatusCode status)
    {
        var clock = new FakeTimeProvider();
        var (client, inner) = Client(new HttpPolicy(clock));
        inner.Route(Url, status);

        var res = await TestFeeds.Drive(clock, client.GetAsync(Url, Ct));

        Assert.Equal(status, res.StatusCode);
        Assert.Equal(3, inner.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task NonRetryable_IsTriedOnce(HttpStatusCode status)
    {
        var (client, inner) = Client(new HttpPolicy(new FakeTimeProvider()));
        inner.Route(Url, status);

        var res = await client.GetAsync(Url, Ct);

        Assert.Equal(status, res.StatusCode);
        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task Backoff_WaitsOnTheClock_NotRealTime()
    {
        var clock = new FakeTimeProvider();
        var (client, inner) = Client(new HttpPolicy(clock));
        inner.Route(Url, HttpStatusCode.ServiceUnavailable);

        var call = client.GetAsync(Url, Ct);
        await Task.Delay(50, Ct); // real time passes, fake time does not: the retry must still be waiting
        Assert.Equal(1, inner.RequestCount);
        Assert.False(call.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1.5)); // past the first backoff (1 s plus jitter under 0.5 s)
        for (var i = 0; i < 100 && inner.RequestCount < 2; i++) await Task.Delay(5, Ct);
        Assert.Equal(2, inner.RequestCount);

        await TestFeeds.Drive(clock, call);
        Assert.Equal(3, inner.RequestCount);
    }

    [Fact]
    public async Task Timeout_ThrowsTimeoutException_AfterMaxTries()
    {
        var clock = new FakeTimeProvider();
        var (client, inner) = Client(new HttpPolicy(clock));
        inner.Route(Url, async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new HttpResponseMessage(); });

        var e = await TestFeeds.DriveToFailure(clock, client.GetAsync(Url, Ct));

        Assert.IsType<TimeoutException>(e);
        Assert.Equal(3, inner.RequestCount);
    }

    [Fact]
    public async Task CallerCancellation_IsNotRetried()
    {
        var (client, inner) = Client(new HttpPolicy(new FakeTimeProvider()));
        using var cts = new CancellationTokenSource();
        inner.Route(Url, async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new HttpResponseMessage(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(Url, cts.Token));

        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task Concurrency_IsCappedAtEight()
    {
        var policy = new HttpPolicy(new FakeTimeProvider());
        var (client, inner) = Client(policy);
        var gate = new TaskCompletionSource();
        inner.Route(Url, async (_, _) => { await gate.Task; return new HttpResponseMessage(HttpStatusCode.OK); });

        var calls = Enumerable.Range(0, 20).Select(_ => client.GetAsync(Url, Ct)).ToList();
        for (var i = 0; i < 200 && inner.RequestCount < 8; i++) await Task.Delay(5, Ct);
        await Task.Delay(50, Ct);
        Assert.Equal(8, inner.RequestCount); // the other 12 wait for a slot
        gate.SetResult();
        await Task.WhenAll(calls);

        Assert.Equal(8, inner.MaxInFlight);
        Assert.Equal(20, inner.RequestCount);
    }

    [Fact]
    public void Defaults_MatchGlobalConstraints()
    {
        var policy = new HttpPolicy(TimeProvider.System);
        Assert.Equal(3, policy.MaxTries);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.PerCallTimeout);
        Assert.Equal(8, HttpPolicy.MaxConcurrency);
    }
}
