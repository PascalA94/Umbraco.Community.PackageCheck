using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PackageCheck.Tests.Support;

public enum RecordMode { Replay, Record }

/// <summary>
/// Test-only innermost HTTP handler. Replay mode answers from files of the shape
/// <c>{ "method", "url", "status", "contentType", "body" | "bodyBase64" }</c> and never touches the network.
/// Record mode (<c>PACKAGECHECK_RECORD=1</c>) forwards to the live host and writes those files. Only the method,
/// URL (userinfo and query stripped from the stored copy), status, content type and body are kept: request headers,
/// including Authorization, are never written.
/// Routes registered with <see cref="Route"/> are answered in code first; they model synthetic feeds (401s, 5xx,
/// refused connections) that no recording can produce.
/// </summary>
public sealed class FixtureHttpHandler : HttpMessageHandler
{
    public delegate Task<HttpResponseMessage> RouteHandler(HttpRequestMessage request, CancellationToken ct);

    private readonly string _folder;
    private readonly RecordMode _mode;
    private readonly List<(Func<Uri, bool> Match, RouteHandler Handler)> _routes = [];
    private readonly HttpMessageInvoker? _live;
    private readonly List<string> _requests = [];
    private readonly List<string> _misses = [];
    private readonly List<string?> _userAgents = [];
    private readonly List<string?> _authorizations = [];
    private int _inFlight;
    private int _maxInFlight;

    public FixtureHttpHandler(string folder, RecordMode mode)
    {
        _folder = folder;
        _mode = mode;
        if (mode == RecordMode.Record)
        {
            Directory.CreateDirectory(folder);
            _live = new HttpMessageInvoker(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });
        }
    }

    public static RecordMode ModeFromEnvironment() =>
        Environment.GetEnvironmentVariable("PACKAGECHECK_RECORD") == "1" ? RecordMode.Record : RecordMode.Replay;

    public int RequestCount { get { lock (_requests) return _requests.Count; } }

    /// <summary>Highest number of requests in flight at once (used by Task 28a's concurrency test).</summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public IReadOnlyList<string> Requests { get { lock (_requests) return [.. _requests]; } }
    public IReadOnlyList<string> Misses { get { lock (_requests) return [.. _misses]; } }
    public IReadOnlyList<string?> UserAgents { get { lock (_requests) return [.. _userAgents]; } }
    public IReadOnlyList<string?> Authorizations { get { lock (_requests) return [.. _authorizations]; } }

    public int CountFor(string urlPrefix) => Requests.Count(r => r.StartsWith(urlPrefix, StringComparison.OrdinalIgnoreCase));

    public FixtureHttpHandler Route(string urlPrefix, RouteHandler handler)
    {
        // Matched without userinfo, so a feed URL carrying credentials still reaches its route.
        _routes.Add((u => StripUserInfoAndQuery(u).StartsWith(urlPrefix, StringComparison.OrdinalIgnoreCase), handler));
        return this;
    }

    public FixtureHttpHandler Route(string urlPrefix, HttpStatusCode status, string? json = null) =>
        Route(urlPrefix, (_, _) => Task.FromResult(Json(status, json)));

    public static HttpResponseMessage Json(HttpStatusCode status, string? json) =>
        new(status) { Content = new StringContent(json ?? "", Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        lock (_requests)
        {
            _requests.Add(uri.ToString());
            _userAgents.Add(request.Headers.TryGetValues("User-Agent", out var ua) ? string.Join(" ", ua) : null);
            _authorizations.Add(request.Headers.Authorization?.ToString());
        }

        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen) { }
        try
        {
            foreach (var (match, handler) in _routes)
                if (match(uri))
                    return await handler(request, ct);

            var file = Path.Combine(_folder, FileNameFor(request.Method, uri));
            if (_mode == RecordMode.Record)
                return await RecordAsync(request, file, ct);

            if (!File.Exists(file))
            {
                lock (_requests) _misses.Add($"{request.Method} {uri}");
                throw new InvalidOperationException($"No HTTP fixture for {request.Method} {uri} (expected {file}); record with PACKAGECHECK_RECORD=1.");
            }
            return Replay(file);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task<HttpResponseMessage> RecordAsync(HttpRequestMessage request, string file, CancellationToken ct)
    {
        // A fresh request without the caller's headers: nothing credential-shaped reaches the live host or the file.
        using var live = new HttpRequestMessage(request.Method, request.RequestUri);
        live.Headers.UserAgent.ParseAdd("umbraco-package-check-fixture-recorder");
        using var res = await _live!.SendAsync(live, ct);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        var contentType = res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var node = new JsonObject
        {
            ["method"] = request.Method.Method,
            ["url"] = StripUserInfoAndQuery(request.RequestUri!),
            ["status"] = (int)res.StatusCode,
            ["contentType"] = contentType,
        };
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            node["body"] = Encoding.UTF8.GetString(bytes);
        else
            node["bodyBase64"] = Convert.ToBase64String(bytes);
        await File.WriteAllTextAsync(file, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
        return Build((int)res.StatusCode, contentType, bytes);
    }

    private static HttpResponseMessage Replay(string file)
    {
        var node = JsonNode.Parse(File.ReadAllText(file))!;
        var bytes = node["bodyBase64"] is { } b64
            ? Convert.FromBase64String(b64.GetValue<string>())
            : Encoding.UTF8.GetBytes(node["body"]?.GetValue<string>() ?? "");
        return Build(node["status"]!.GetValue<int>(), node["contentType"]?.GetValue<string>() ?? "application/json", bytes);
    }

    private static HttpResponseMessage Build(int status, string contentType, byte[] bytes)
    {
        var res = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(bytes) };
        res.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return res;
    }

    private static string StripUserInfoAndQuery(Uri u) =>
        u.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);

    /// <summary>Readable, stable file name: host plus path, with a short hash so long or odd paths stay unique.</summary>
    public static string FileNameFor(HttpMethod method, Uri u)
    {
        var key = $"{method.Method} {StripUserInfoAndQuery(u)}{u.Query}";
        var readable = Regex.Replace($"{u.Host}{u.AbsolutePath}".ToLowerInvariant(), "[^a-z0-9.-]+", "_").Trim('_');
        if (readable.Length > 120) readable = readable[..120];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
        return $"{readable}_{hash}.json";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _live?.Dispose();
        base.Dispose(disposing);
    }
}
