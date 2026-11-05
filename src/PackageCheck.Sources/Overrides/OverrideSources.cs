using PackageCheck.Core;
using PackageCheck.Core.Overrides;
using PackageCheck.Sources.Data;
using PackageCheck.Sources.NuGet;

namespace PackageCheck.Sources.Overrides;

/// <summary>The seed shipped in the tool (design 7.8); its entries are community overrides.</summary>
public sealed class BundledOverrideSource : IOverrideSource
{
    public Task<OverrideSet> LoadAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(BundledData.Open(BundledData.OverridesSeedResource));
        return Task.FromResult(OverrideFileParser.Parse(reader.ReadToEnd(), "bundled overrides snapshot", OverrideOrigin.Community));
    }
}

/// <summary>
/// <c>--overrides &lt;file|url&gt;</c>: a path is read from disk; an https URL is fetched once with a 30 s timeout. Any
/// other scheme is a usage error. A failed fetch is a warning and an empty set, never exit 3 (design 7.3).
/// </summary>
public sealed class LocalOverrideSource : IOverrideSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly string _fileOrUrl;
    private readonly Uri? _url;
    private readonly HttpClient _http;
    private readonly Action<string>? _onWarning;

    public LocalOverrideSource(string fileOrUrl, HttpClient http, Action<string>? onWarning = null)
    {
        _fileOrUrl = fileOrUrl;
        _http = http;
        _onWarning = onWarning;
        if (Uri.TryCreate(fileOrUrl, UriKind.Absolute, out var u) && !u.IsFile)
        {
            if (u.Scheme != Uri.UriSchemeHttps)
                throw new UsageException($"--overrides must be a file path or an https URL, not {u.Scheme}://");
            _url = u;
        }
    }

    public async Task<OverrideSet> LoadAsync(CancellationToken ct)
    {
        if (_url is null)
        {
            if (!File.Exists(_fileOrUrl)) throw new PathNotFoundException(_fileOrUrl);
            return OverrideFileParser.Parse(await File.ReadAllTextAsync(_fileOrUrl, ct), Path.GetFileName(_fileOrUrl), OverrideOrigin.Local, Warnings());
        }

        var shown = FeedSettings.Redact(_url.ToString());
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var res = await _http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                _onWarning?.Invoke($"local overrides from {shown} could not be fetched (HTTP {(int)res.StatusCode}); continuing without them");
                return new OverrideSet([], shown);
            }
            var body = await ReadCappedAsync(res.Content, cts.Token);
            try
            {
                return OverrideFileParser.Parse(body, shown, OverrideOrigin.Local, Warnings());
            }
            catch (UsageException)
            {
                // Design 7.3 / security review M4-S9: a remote file the user cannot fix here degrades the run; a bad
                // local file stays a usage error (exit 2).
                _onWarning?.Invoke($"local overrides from {shown} could not be read (not a valid overrides file); continuing without them");
                return new OverrideSet([], shown);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidDataException or IOException)
        {
            _onWarning?.Invoke($"local overrides from {shown} could not be fetched ({e.GetType().Name}); continuing without them");
            return new OverrideSet([], shown);
        }
    }

    private List<string> Warnings() => new WarningList(_onWarning);

    private static async Task<string> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[OverrideFileParser.MaxBytes + 1];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
        {
            total += read;
            if (total > OverrideFileParser.MaxBytes) throw new InvalidDataException("overrides response too large");
        }
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// <summary>Forwards parser warnings to the callback as they are added.</summary>
    private sealed class WarningList(Action<string>? onWarning) : List<string>, ICollection<string>
    {
        void ICollection<string>.Add(string item)
        {
            base.Add(item);
            onWarning?.Invoke(item);
        }
    }
}

/// <summary>Merges sources in order; a later source's entry replaces an earlier one for the same id and ranges.</summary>
public sealed class CompositeOverrideSource(IReadOnlyList<IOverrideSource> sources) : IOverrideSource
{
    public async Task<OverrideSet> LoadAsync(CancellationToken ct)
    {
        var merged = new List<OverrideEntry>();
        var labels = new List<string>();
        foreach (var source in sources)
        {
            var set = await source.LoadAsync(ct);
            if (set.SourceLabel is not null) labels.Add(set.SourceLabel);
            foreach (var e in set.Entries)
            {
                merged.RemoveAll(m => m.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase)
                                      && m.PackageVersions.Equals(e.PackageVersions) && m.Umbraco.Equals(e.Umbraco));
                merged.Add(e);
            }
        }
        return new OverrideSet(merged, labels.Count == 0 ? null : string.Join(" + ", labels));
    }
}
