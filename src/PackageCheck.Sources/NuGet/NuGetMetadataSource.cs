using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Sources.Http;

namespace PackageCheck.Sources.NuGet;

/// <summary>
/// <see cref="IPackageMetadataSource"/> on NuGet.Protocol (design 7.3, spike S1). Each package is looked up across the
/// feeds its source mapping allows, in config order; the first feed holding it wins and serves every later call for
/// that id in this run. When none does, the outcome follows the design 7.3 multi-feed table:
/// any feed that failed (5xx/429 after retries, timeout, connection) gives source-error, because it might hold the
/// package; else any 401/403 gives private-feed; else not-found.
/// </summary>
public sealed class NuGetMetadataSource : IPackageMetadataSource, IDisposable
{
    // Unlisted nuget.org versions report this date instead of their release date.
    private static readonly DateTimeOffset UnlistedSentinel = new(1901, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly FeedSettings _feeds;
    private readonly ArchiveLimits _limits;
    private readonly HttpMessageHandler _network;
    private readonly bool _ownsNetwork;
    private readonly Func<PackageSource, HttpMessageHandler> _chain;
    private readonly ConcurrentDictionary<string, SourceRepository> _repositories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<Registration>>> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public NuGetMetadataSource(FeedSettings feeds, HttpPolicy policy, HttpMessageHandler? testHandler, ArchiveLimits? limits = null)
    {
        _feeds = feeds;
        _limits = limits ?? ArchiveLimits.Default;
        _ownsNetwork = testHandler is null;
        // NuGet's own handler turns on decompression; a replacement must too, or registration JSON arrives gzipped (S1).
        _network = testHandler ?? new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
        _chain = source => policy.CreateHandler(new FeedCredentialHandler(source, new LeaveOpenHandler(_network)));
    }

    public async Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct)
    {
        ValidateId(id);
        var registration = await RegistrationAsync(id, ct);
        var versions = registration.Items.Select(m => m.Identity.Version).Distinct().Order().ToList();
        return new PackageVersions(id, versions);
    }

    public async Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct)
    {
        ValidateId(id);
        var registration = await RegistrationAsync(id, ct);
        var item = registration.Items.FirstOrDefault(m => m.Identity.Version == v);
        if (item is null) return null;

        var deprecation = await item.GetDeprecationMetadataAsync();
        return new PackageDetails(
            new PackageIdentity(item.Identity.Id, item.Identity.Version),
            item.DependencySets.ToList(),
            deprecation is null
                ? null
                : new DeprecationInfo(
                    deprecation.Reasons?.ToList() ?? [],
                    deprecation.AlternatePackage?.PackageId,
                    deprecation.AlternatePackage?.Range),
            item.Published is { } p && p >= UnlistedSentinel ? p : null,
            item.IsListed);
    }

    public async Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct)
    {
        ValidateId(id);
        // Design 7.5 step 1: a restored site already has most nupkgs; reading them costs no HTTP at all.
        var info = GlobalPackagesFile(_feeds.GlobalPackagesFolder, id, v);
        if (info is not null)
        {
            if (info.Length > _limits.MaxNupkgBytes) throw new NupkgTooLargeException(id, v, _limits.MaxNupkgBytes);
            return new NupkgHandle(info.FullName, IsTemporary: false, info.Length);
        }

        var found = await FirstFeedAsync(id, (repo, token) => DownloadAsync(repo, id, v, token), throwWhenNotFound: false, ct);
        return found?.Value;
    }

    private Task<Registration> RegistrationAsync(string id, CancellationToken ct)
    {
        // One registration read per id per run, shared by GetVersions and GetDetails and by concurrent callers.
        var lazy = _registrations.GetOrAdd(id, key => new Lazy<Task<Registration>>(() => LoadRegistrationAsync(key, ct)));
        return lazy.Value;
    }

    private async Task<Registration> LoadRegistrationAsync(string id, CancellationToken ct)
    {
        var found = await FirstFeedAsync(id, async (repo, token) =>
        {
            var meta = await repo.GetResourceAsync<PackageMetadataResource>(token);
            if (meta is null) return null; // a feed type without registration metadata cannot hold the answer
            using var cache = NewCacheContext();
            // A feed answering for another id is ignored: the id later builds paths and URLs (security review M3, S5).
            var items = (await meta.GetMetadataAsync(id, includePrerelease: true, includeUnlisted: true, cache, NullLogger.Instance, token))
                .Where(m => string.Equals(m.Identity.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
            return items.Count == 0 ? null : items;
        }, throwWhenNotFound: true, ct);
        return new Registration(found!.Value.Feed, found.Value.Value);
    }

    private async Task<NupkgHandle?> DownloadAsync(SourceRepository repo, string id, NuGetVersion v, CancellationToken ct)
    {
        Directory.CreateDirectory(_feeds.TempFolder);
        var path = Path.Combine(_feeds.TempFolder, $"{Guid.NewGuid():N}.nupkg");
        var keep = false;
        try
        {
            bool copied;
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            await using (var capped = new CappedWriteStream(file, _limits.MaxNupkgBytes, () => new NupkgTooLargeException(id, v, _limits.MaxNupkgBytes)))
            {
                copied = await CopyNupkgAsync(repo, id, v, capped, ct);
            }
            if (!copied) return null;
            keep = true;
            return new NupkgHandle(path, IsTemporary: true, new FileInfo(path).Length);
        }
        finally
        {
            // Review focus 5: a failed or abandoned download never leaves a temp nupkg behind.
            if (!keep) File.Delete(path);
        }
    }

    /// <summary>
    /// v3 feeds: one GET of the flat-container nupkg URL through the policy chain. NuGet's FindPackageByIdResource is
    /// avoided there because it runs its own 6-try loop (stacking on HttpPolicy to 18 attempts) and first fetches the
    /// version index, a second request per package. Local folder and v2 feeds fall back to it.
    /// </summary>
    private async Task<bool> CopyNupkgAsync(SourceRepository repo, string id, NuGetVersion v, Stream destination, CancellationToken ct)
    {
        var index = await repo.GetResourceAsync<ServiceIndexResourceV3>(ct);
        var baseUri = index?.GetServiceEntryUri(ServiceTypes.PackageBaseAddress);
        if (baseUri is null)
        {
            var finder = await repo.GetResourceAsync<FindPackageByIdResource>(ct);
            if (finder is null) return false;
            using var cache = NewCacheContext();
            return await finder.CopyNupkgToStreamAsync(id, v, destination, cache, NullLogger.Instance, ct);
        }

        var lowerId = Uri.EscapeDataString(id.ToLowerInvariant());
        var lowerVersion = Uri.EscapeDataString(v.ToNormalizedString().ToLowerInvariant());
        var url = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + $"/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg");

        using var invoker = new HttpMessageInvoker(_chain(repo.PackageSource), disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await invoker.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("nupkg download failed", null, response.StatusCode);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await body.CopyToAsync(destination, ct);
        return true;
    }

    private async Task<(T Value, PackageSource Feed)?> FirstFeedAsync<T>(
        string id, Func<SourceRepository, CancellationToken, Task<T?>> query, bool throwWhenNotFound, CancellationToken ct)
        where T : class
    {
        var feeds = _feeds.SourcesFor(id);
        var denied = new List<PackageSource>();
        var failed = new List<(PackageSource Feed, string What)>();

        foreach (var feed in feeds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var value = await query(Repository(feed), ct);
                if (value is not null) return (value, feed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (Find<NupkgTooLargeException>(e) is { } tooLarge)
            {
                throw tooLarge;
            }
            catch (Exception e)
            {
                switch (SourceErrorClassifier.Classify(e))
                {
                    case UnknownReason.PrivateFeed: denied.Add(feed); break;
                    case UnknownReason.NotFound: break;
                    default: failed.Add((feed, Describe(e))); break;
                }
            }
        }

        // Messages name feeds by key and redacted URL only; NuGet's own messages are never copied, because they can
        // carry the configured URL with its userinfo or query token (global constraints, Security).
        if (failed.Count > 0)
            throw new PackageSourceException(UnknownReason.SourceError,
                $"Could not read '{id}': " + string.Join("; ", failed.Select(f => $"{Name(f.Feed)} {f.What}")));
        if (denied.Count > 0)
            throw new PackageSourceException(UnknownReason.PrivateFeed,
                $"'{id}' is not readable: {string.Join(", ", denied.Select(Name))} refused access (401/403). " +
                "Private feeds need credentials in NuGet.config packageSourceCredentials.");
        if (!throwWhenNotFound) return null;
        throw new PackageSourceException(UnknownReason.NotFound, feeds.Count == 0
            ? $"'{id}' is not mapped to any configured feed"
            : $"'{id}' was not found on {string.Join(", ", feeds.Select(Name))}");
    }

    private SourceRepository Repository(PackageSource feed) =>
        _repositories.GetOrAdd(feed.Name, _ => NuGetSeam.Repository(feed, _chain, _feeds.HttpCacheFolder));

    // No NuGet disk cache reads or writes: Task 23 owns caching, keyed and redacted our way (design 7.5).
    private static SourceCacheContext NewCacheContext() => new() { NoCache = true, DirectDownload = true };

    private static string Name(PackageSource feed) => $"'{feed.Name}' ({FeedSettings.Redact(feed.Source)})";

    private static string Describe(Exception e)
    {
        if (Find<TimeoutException>(e) is not null) return "timed out";
        if (Find<HttpRequestException>(e) is { StatusCode: { } code }) return $"answered HTTP {(int)code}";
        if (Find<SocketException>(e) is not null || Find<HttpRequestException>(e) is not null) return "could not be reached";
        return "returned an unreadable response";
    }

    private static TEx? Find<TEx>(Exception e) where TEx : Exception
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            if (x is TEx hit) return hit;
            if (x is AggregateException agg)
                foreach (var inner in agg.InnerExceptions)
                    if (Find<TEx>(inner) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>
    /// Security review M3 (S5): ids come from project files and feed JSON. Only valid NuGet ids reach a path or URL; the
    /// id itself is left out of the message because it is untrusted text.
    /// </summary>
    public static void ValidateId(string id)
    {
        if (!PackageIdValidator.IsValidPackageId(id))
            throw new PackageSourceException(UnknownReason.NotFound, "the package id is not a valid NuGet package id");
    }

    /// <summary>
    /// The nupkg in a global packages folder, or null. The path must stay inside the folder and be a regular file: a
    /// symlink or device could point anywhere (a FIFO would hang the hash).
    /// </summary>
    public static FileInfo? GlobalPackagesFile(string globalPackagesFolder, string id, NuGetVersion v)
    {
        var root = Path.GetFullPath(globalPackagesFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(new VersionFolderPathResolver(globalPackagesFolder).GetPackageFilePath(id, v));
        if (!path.StartsWith(root, StringComparison.Ordinal)) return null;
        var info = new FileInfo(path);
        return info.Exists && info.LinkTarget is null && (info.Attributes & (FileAttributes.Device | FileAttributes.ReparsePoint)) == 0 ? info : null;
    }

    public void Dispose()
    {
        if (_feeds.OwnsTempFolder)
        {
            try { Directory.Delete(_feeds.TempFolder, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        if (_ownsNetwork) _network.Dispose();
    }

    private sealed record Registration(PackageSource Feed, IReadOnlyList<IPackageSearchMetadata> Items);
}
