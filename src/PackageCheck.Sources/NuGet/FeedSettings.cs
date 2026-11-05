using System.Text.RegularExpressions;
using NuGet.Configuration;

namespace PackageCheck.Sources.NuGet;

/// <summary>
/// The feeds one site uses. Read from the NuGet.config nearest the site (design 7.3, preflight 1.18): NuGet merges
/// configs walking up from the site folder, so a config deeper in the tree (next to one project) never applies.
/// Folders are the tool's own so a warm machine-wide HTTP cache never answers beneath the handler (ruling R22).
/// </summary>
public sealed partial class FeedSettings
{
    private readonly PackageSourceMapping _mapping;

    private FeedSettings(ISettings settings, string globalPackagesFolder, string httpCacheFolder, string tempFolder, bool ownsTempFolder)
    {
        OwnsTempFolder = ownsTempFolder;
        Settings = settings;
        // Disabled sources are skipped exactly as restore skips them.
        Sources = new PackageSourceProvider(settings).LoadPackageSources().Where(s => s.IsEnabled).ToList();
        _mapping = PackageSourceMapping.GetPackageSourceMapping(settings);
        GlobalPackagesFolder = globalPackagesFolder;
        HttpCacheFolder = httpCacheFolder;
        TempFolder = tempFolder;
    }

    public ISettings Settings { get; }

    /// <summary>Enabled sources in config order; the first that holds a package wins.</summary>
    public IReadOnlyList<PackageSource> Sources { get; }

    /// <summary>Global packages folder (honours NUGET_PACKAGES and globalPackagesFolder, as restore does).</summary>
    public string GlobalPackagesFolder { get; }

    /// <summary>The tool's own NuGet HTTP cache folder.</summary>
    public string HttpCacheFolder { get; }

    /// <summary>Where downloaded nupkgs are written while inspected; each is deleted after use.</summary>
    public string TempFolder { get; }

    /// <summary>True when <see cref="TempFolder"/> was created for this run, so the source deletes it when disposed.</summary>
    public bool OwnsTempFolder { get; }

    public static FeedSettings ForSite(string sitePath, string? globalPackagesFolder = null, string? httpCacheFolder = null, string? tempFolder = null) =>
        FromSettings(global::NuGet.Configuration.Settings.LoadDefaultSettings(Path.GetFullPath(sitePath)), globalPackagesFolder, httpCacheFolder, tempFolder);

    public static FeedSettings FromSettings(ISettings settings, string? globalPackagesFolder = null, string? httpCacheFolder = null, string? tempFolder = null)
    {
        var toolRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "umbraco-package-check");
        return new FeedSettings(
            settings,
            globalPackagesFolder ?? SettingsUtility.GetGlobalPackagesFolder(settings),
            httpCacheFolder ?? Path.Combine(toolRoot, "nuget-http"),
            // Security review M3 (S6): a fixed /tmp path can be pre-created by another user, who could then swap a nupkg
            // between hashing and inspection. A per-run folder is unpredictable and 0700; the source deletes it on dispose.
            tempFolder ?? Directory.CreateTempSubdirectory("umbraco-package-check-").FullName,
            ownsTempFolder: tempFolder is null);
    }

    /// <summary>
    /// Sources allowed for <paramref name="id"/>. NuGet.Protocol does not apply package source mapping when
    /// repositories are built by hand (spike S1, step 5), so it is applied here. With mapping on, an id matching no
    /// pattern gets no sources, as restore would.
    /// </summary>
    public IReadOnlyList<PackageSource> SourcesFor(string id)
    {
        if (!_mapping.IsEnabled) return Sources;
        var allowed = _mapping.GetConfiguredPackageSources(id);
        return Sources.Where(s => allowed.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// A feed URL safe to print: scheme, host and port only (global constraints, Security). Tokens live in userinfo,
    /// queries and paths too (Cloudsmith entitlement URLs, MyGet /auth/&lt;key&gt;/), so no path is ever shown; messages
    /// name the feed by its NuGet.config key as well. Userinfo is cut by hand at the last '@' before the query, because
    /// Uri parses "user:pa/ss@host" with "user" as the host. Non-URL strings (local folder feeds) keep their path.
    /// </summary>
    public static string Redact(string url)
    {
        var cut = url.IndexOfAny(['?', '#']);
        var noQuery = cut < 0 ? url : url[..cut];
        var scheme = noQuery.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0 && (noQuery[..scheme].Equals("http", StringComparison.OrdinalIgnoreCase)
                           || noQuery[..scheme].Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            var rest = noQuery[(scheme + 3)..];
            var at = rest.LastIndexOf('@');
            if (at >= 0) rest = rest[(at + 1)..];
            var slash = rest.IndexOf('/');
            var authority = slash < 0 ? rest : rest[..slash];
            return $"{noQuery[..scheme].ToLowerInvariant()}://{authority.ToLowerInvariant()}";
        }
        return UserInfo().Replace(noQuery, "://");
    }

    [GeneratedRegex(@"://[^/@]*@")]
    private static partial Regex UserInfo();
}
