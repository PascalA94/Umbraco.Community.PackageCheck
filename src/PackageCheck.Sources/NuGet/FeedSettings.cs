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

    private FeedSettings(ISettings settings, string globalPackagesFolder, string httpCacheFolder, string tempFolder)
    {
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

    public static FeedSettings ForSite(string sitePath, string? globalPackagesFolder = null, string? httpCacheFolder = null, string? tempFolder = null) =>
        FromSettings(global::NuGet.Configuration.Settings.LoadDefaultSettings(Path.GetFullPath(sitePath)), globalPackagesFolder, httpCacheFolder, tempFolder);

    public static FeedSettings FromSettings(ISettings settings, string? globalPackagesFolder = null, string? httpCacheFolder = null, string? tempFolder = null)
    {
        var toolRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "umbraco-package-check");
        return new FeedSettings(
            settings,
            globalPackagesFolder ?? SettingsUtility.GetGlobalPackagesFolder(settings),
            httpCacheFolder ?? Path.Combine(toolRoot, "nuget-http"),
            tempFolder ?? Path.Combine(Path.GetTempPath(), "umbraco-package-check"));
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
    /// A feed URL safe to print: userinfo, query and fragment removed (global constraints, Security). Tokens live in
    /// those parts (Azure SAS-style query keys, user:pat@host). Non-URL strings get the same treatment by pattern.
    /// </summary>
    public static string Redact(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);

        var cut = url.IndexOfAny(['?', '#']);
        var noQuery = cut < 0 ? url : url[..cut];
        return UserInfo().Replace(noQuery, "://");
    }

    [GeneratedRegex(@"://[^/@]*@")]
    private static partial Regex UserInfo();
}
