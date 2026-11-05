using System.Net;
using PackageCheck.Core;

namespace PackageCheck.Sources.Http;

/// <summary>
/// The one HTTP policy for every outbound call (global constraints; design 7.3): at most <see cref="MaxConcurrency"/>
/// requests in flight across all handlers made by this instance, retry on 429 and 5xx and on a timed-out attempt,
/// at most <see cref="MaxTries"/> tries, a per-attempt timeout, and the tool's User-Agent. Later HttpClients
/// (Marketplace, overrides) reuse it through <see cref="CreateHandler"/>.
/// Backoff and timeouts run on the injected <see cref="TimeProvider"/>, so tests use FakeTimeProvider and never sleep
/// (plan review P14).
/// </summary>
public sealed class HttpPolicy(TimeProvider clock, int maxTries = 3, TimeSpan? perCallTimeout = null)
{
    public const int MaxConcurrency = 8;

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _slots = new(MaxConcurrency, MaxConcurrency);

    public TimeProvider Clock { get; } = clock;
    public int MaxTries { get; } = maxTries >= 1 ? maxTries : throw new ArgumentOutOfRangeException(nameof(maxTries));
    public TimeSpan PerCallTimeout { get; } = perCallTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>Wraps <paramref name="inner"/> (the network handler, or a test handler) in this policy.</summary>
    public DelegatingHandler CreateHandler(HttpMessageHandler inner) => new PolicyHandler(this, inner);

    /// <summary>1 s, 2 s, 4 s ... before retry n, plus up to 250 ms of jitter so parallel retries spread out.</summary>
    internal static TimeSpan Backoff(int failedAttempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, failedAttempt - 1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));

    internal static bool IsRetryable(HttpStatusCode code) => code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private sealed class PolicyHandler(HttpPolicy policy, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Replace, not append: NuGet.Protocol sets its own client-wide User-Agent (preflight 1.19).
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", ToolInfo.UserAgent);

            for (var attempt = 1; ; attempt++)
            {
                var (response, timedOut) = await AttemptAsync(request, ct);

                if (!timedOut && !IsRetryable(response!.StatusCode)) return response;
                if (attempt >= policy.MaxTries)
                {
                    if (timedOut)
                        throw new TimeoutException($"HTTP request timed out after {policy.PerCallTimeout.TotalSeconds:0} s ({policy.MaxTries} tries)");
                    return response!; // the caller sees the final 429/5xx and classifies it as source-error
                }

                var delay = RetryAfter(response) ?? Backoff(attempt);
                response?.Dispose();
                await Task.Delay(delay, policy.Clock, ct);
            }
        }

        private async Task<(HttpResponseMessage? Response, bool TimedOut)> AttemptAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // The slot covers one attempt only, never a backoff wait, so a retrying feed does not starve the others.
            await policy._slots.WaitAsync(ct);
            HttpResponseMessage? response = null;
            try
            {
                using var timeout = new CancellationTokenSource(policy.PerCallTimeout, policy.Clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                response = await base.SendAsync(request, linked.Token);
                if (ShouldBuffer(request))
                    await BufferAsync(response, linked.Token);
                return (response, false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                response?.Dispose();
                return (null, true);
            }
            finally
            {
                policy._slots.Release();
            }
        }

        /// <summary>
        /// Metadata bodies are read inside the attempt, so the 30 s timeout covers the whole call, not just the
        /// headers. Nupkgs are streamed instead (up to 250 MB, capped by the caller's counting stream), never buffered.
        /// </summary>
        private static bool ShouldBuffer(HttpRequestMessage request) =>
            !request.RequestUri!.AbsolutePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        private static async Task BufferAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var original = response.Content;
            var bytes = await original.ReadAsByteArrayAsync(ct);
            var buffered = new ByteArrayContent(bytes);
            foreach (var header in original.Headers)
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = buffered;
            original.Dispose();
        }

        private TimeSpan? RetryAfter(HttpResponseMessage? response)
        {
            var header = response?.Headers.RetryAfter;
            var wait = header?.Delta ?? (header?.Date is { } at ? at - policy.Clock.GetUtcNow() : null);
            if (wait is not { } w || w <= TimeSpan.Zero) return null;
            return w < MaxRetryAfter ? w : MaxRetryAfter;
        }
    }
}
