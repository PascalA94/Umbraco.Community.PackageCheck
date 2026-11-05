using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace PackageCheck.Sources.NuGet;

/// <summary>
/// The HTTP seam (ruling R22, spike S1): NuGet.Protocol resolves <see cref="HttpHandlerResource"/> and
/// <see cref="HttpSourceResource"/> through resource providers, so replacing those two providers routes every request
/// for a repository through our handler chain without touching NuGet internals.
/// </summary>
internal static class NuGetSeam
{
    private static readonly Type[] Replaced = [typeof(HttpHandlerResource), typeof(HttpSourceResource), typeof(ServiceIndexResourceV3)];

    public static SourceRepository Repository(PackageSource source, Func<PackageSource, HttpMessageHandler> chain, string httpCacheFolder)
    {
        var providers = global::NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3()
            .Where(p => !Replaced.Contains(p.Value.ResourceType))
            .ToList();
        providers.Add(new Lazy<INuGetResourceProvider>(() => new HandlerProvider(chain)));
        providers.Add(new Lazy<INuGetResourceProvider>(() => new HttpSourceProvider(httpCacheFolder)));
        providers.Add(new Lazy<INuGetResourceProvider>(() => new ServiceIndexProvider()));
        return new SourceRepository(source, providers);
    }

    private sealed class HandlerProvider(Func<PackageSource, HttpMessageHandler> chain)
        : ResourceProvider(typeof(HttpHandlerResource), nameof(HandlerProvider), NuGetResourceProviderPositions.Last)
    {
        public override Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
        {
            // The first argument only feeds NuGet's proxy/credential plumbing, which this seam bypasses on purpose:
            // no credential service is ever set up, so nothing can prompt (spike S1b fallback).
            var resource = new HttpHandlerResourceV3(new HttpClientHandler(), chain(source.PackageSource));
            return Task.FromResult(new Tuple<bool, INuGetResource?>(true, resource));
        }
    }

    private sealed class HttpSourceProvider(string httpCacheFolder)
        : ResourceProvider(typeof(HttpSourceResource), nameof(HttpSourceProvider), NuGetResourceProviderPositions.Last)
    {
        public override Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
        {
            var http = new HttpSource(source.PackageSource, HandlerFactory(source), NullThrottle.Instance)
            {
                // NuGet's own retry (up to 6 tries with real sleeps, NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT) would stack
                // on HttpPolicy and give 3 x 6 attempts (preflight row 12). HttpPolicy is the only retry layer.
                RetryHandler = new SingleAttemptRetryHandler(),
                // The tool's own cache folder, never the machine-wide one, so a warm cache cannot hide a request
                // from the handler (spike S1 finding; ruling R22).
                HttpCacheDirectory = httpCacheFolder,
            };
            return Task.FromResult(new Tuple<bool, INuGetResource?>(true, new HttpSourceResource(http)));
        }
    }

    /// <summary>
    /// NuGet's ServiceIndexResourceV3Provider wraps the index fetch in its own loop of up to 6 tries
    /// (NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT), which stacked on HttpPolicy gave 18 attempts. This one fetches once per
    /// repository through the seam's HttpSource and leaves retrying to HttpPolicy, so a failing feed sees exactly 3.
    /// It also skips NuGet's 40-minute disk cache of the index. Non-HTTP (local folder) and v2 sources get no index,
    /// as with NuGet's provider.
    /// </summary>
    private sealed class ServiceIndexProvider()
        : ResourceProvider(typeof(ServiceIndexResourceV3), nameof(ServiceIndexProvider), NuGetResourceProviderPositions.Last)
    {
        // One fetch per repository and run. A feed whose index failed stays failed for the run, so a dead feed costs
        // 3 attempts in total rather than 3 per package.
        private Lazy<Task<ServiceIndexResourceV3>>? _index;

        public override async Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
        {
            var url = source.PackageSource.Source;
            if (!source.PackageSource.IsHttp || !url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return new Tuple<bool, INuGetResource?>(false, null);

            var lazy = LazyInitializer.EnsureInitialized(ref _index, () => new Lazy<Task<ServiceIndexResourceV3>>(() => LoadAsync(source, url)));
            return new Tuple<bool, INuGetResource?>(true, await lazy!.Value.WaitAsync(token));
        }

        private static async Task<ServiceIndexResourceV3> LoadAsync(SourceRepository source, string url)
        {
            var http = (await source.GetResourceAsync<HttpSourceResource>(CancellationToken.None))!.HttpSource;
            var json = await http.GetJObjectAsync(new HttpSourceRequest(url, NullLogger.Instance), NullLogger.Instance, CancellationToken.None);
            var version = json?.Value<string>("version");
            if (json is null || version is null || !version.StartsWith("3.", StringComparison.Ordinal))
                throw new InvalidDataException("The feed did not return a NuGet v3 service index.");
            return new ServiceIndexResourceV3(json, DateTime.UtcNow);
        }
    }

    // The factory runs lazily on the first request, long after TryCreate's token, so it must not capture that token.
    private static Func<Task<HttpHandlerResource>> HandlerFactory(SourceRepository source) => async () =>
        await source.GetResourceAsync<HttpHandlerResource>(CancellationToken.None)
        ?? throw new InvalidOperationException("HttpHandlerResource provider missing");

    private sealed class SingleAttemptRetryHandler : IHttpRetryHandler
    {
        public Task<HttpResponseMessage> SendAsync(HttpRetryHandlerRequest request, ILogger log, CancellationToken cancellationToken) =>
            SendAsync(request, string.Empty, log, cancellationToken);

        public Task<HttpResponseMessage> SendAsync(HttpRetryHandlerRequest request, string source, ILogger log, CancellationToken cancellationToken)
        {
            var message = request.RequestFactory();
            foreach (var header in request.AddHeaders)
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return request.HttpClient.SendAsync(message, request.CompletionOption, cancellationToken);
        }
    }
}

/// <summary>
/// Adds NuGet.config packageSourceCredentials as preemptive Basic auth (spike S1b fallback: no credential providers in
/// 0.1). Sent only to the feed's own scheme, host and port, so a service index that points elsewhere cannot collect
/// the feed's credentials. A password that cannot be read (an encrypted one off Windows) means no credentials, which
/// surfaces as private-feed.
/// </summary>
internal sealed class FeedCredentialHandler : DelegatingHandler
{
    private readonly Uri _feed;
    private readonly string? _header;

    public FeedCredentialHandler(PackageSource source, HttpMessageHandler inner) : base(inner)
    {
        _feed = source.SourceUri;
        _header = BuildHeader(source.Credentials);
    }

    public static string? BuildHeader(PackageSourceCredential? credential)
    {
        if (credential is null || string.IsNullOrEmpty(credential.Username)) return null;
        // Only Basic is supported without a credential provider; a source restricted to other schemes gets nothing.
        if (credential.ValidAuthenticationTypes.Any() &&
            !credential.ValidAuthenticationTypes.Contains("basic", StringComparer.OrdinalIgnoreCase))
            return null;

        string password;
        try { password = credential.Password; }
        catch (Exception e) when (e is NotSupportedException or PlatformNotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        if (string.IsNullOrEmpty(password)) return null;

        // NuGet.Configuration has already expanded %ENV% references (how CI passes feed tokens, S1b); expanding again
        // would corrupt a token that happens to contain %NAME%. Credentials_FromNuGetConfig_..._WithEnvExpansion guards it.
        return "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{credential.Username}:{password}"));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_header is not null && request.Headers.Authorization is null && SameServer(request.RequestUri!, _feed))
            request.Headers.TryAddWithoutValidation("Authorization", _header);
        return base.SendAsync(request, ct);
    }

    private static bool SameServer(Uri a, Uri b) =>
        Uri.Compare(a, b, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}

/// <summary>Lets several handler chains share one network handler without the first disposed chain disposing it.</summary>
internal sealed class LeaveOpenHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override void Dispose(bool disposing) { }
}

/// <summary>
/// Counts bytes written and throws the supplied exception past the limit, so an oversized nupkg stops downloading at
/// the cap instead of filling the disk (design 4: 250 MB).
/// </summary>
internal sealed class CappedWriteStream(Stream inner, long limit, Func<Exception> tooLarge) : Stream
{
    private long _written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Count(count);
        inner.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Count(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Count(int count)
    {
        _written += count;
        if (_written > limit) throw tooLarge();
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
