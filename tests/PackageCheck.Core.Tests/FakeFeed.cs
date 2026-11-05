using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Core.Tests;

/// <summary>An in-memory feed of packages with TFM dependency groups, deprecation and listing; counts detail calls.</summary>
internal sealed class FakeFeed : IPackageMetadataSource
{
    private readonly Dictionary<string, Dictionary<NuGetVersion, PackageDetails>> _packages = new(StringComparer.OrdinalIgnoreCase);
    public int DetailCalls { get; private set; }
    public HashSet<string> FailingIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids whose details throw an unexpected (non-source) exception, as a library bug or hostile input might.</summary>
    public HashSet<string> CrashingIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"Id/version" keys whose details fail with a source error, leaving the rest of the id working.</summary>
    public HashSet<string> FailingVersions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="groups">tfm -> dependencies as "Id range", e.g. ("net10.0", ["Umbraco.Cms.Core [17.0.0, 18.0.0)"]).</param>
    public FakeFeed Add(string id, string version, IEnumerable<(string Tfm, string[] Deps)>? groups = null,
        DeprecationInfo? deprecation = null, bool listed = true, DateTimeOffset? published = null)
    {
        var v = NuGetVersion.Parse(version);
        var g = (groups ?? []).Select(x => new PackageDependencyGroup(NuGetFramework.Parse(x.Tfm), x.Deps.Select(ParseDep))).ToList();
        if (!_packages.TryGetValue(id, out var versions)) _packages[id] = versions = new();
        versions[v] = new PackageDetails(new PackageIdentity(id, v), g, deprecation, published, listed);
        return this;
    }

    /// <summary>Shorthand for a package with one net10.0 group.</summary>
    public FakeFeed Add(string id, string version, params string[] deps) => Add(id, version, [("net10.0", deps)]);

    private static PackageDependency ParseDep(string text)
    {
        var space = text.IndexOf(' ');
        return space < 0
            ? new PackageDependency(text, VersionRange.All)
            : new PackageDependency(text[..space], VersionRange.Parse(text[(space + 1)..]));
    }

    public Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct)
    {
        if (FailingIds.Contains(id)) throw new PackageSourceException(UnknownReason.SourceError, "feed failed");
        if (!_packages.TryGetValue(id, out var versions)) throw new PackageSourceException(UnknownReason.NotFound, $"{id} not found");
        return Task.FromResult(new PackageVersions(id, versions.Keys.Order().ToList()));
    }

    public Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct)
    {
        DetailCalls++;
        if (CrashingIds.Contains(id)) throw new InvalidOperationException("boom with package text \u001b[2J");
        if (FailingVersions.Contains($"{id}/{v.ToNormalizedString()}")) throw new PackageSourceException(UnknownReason.SourceError, "feed failed");
        if (FailingIds.Contains(id)) throw new PackageSourceException(UnknownReason.SourceError, "feed failed");
        if (!_packages.TryGetValue(id, out var versions)) throw new PackageSourceException(UnknownReason.NotFound, $"{id} not found");
        return Task.FromResult(versions.GetValueOrDefault(v));
    }

    private readonly Dictionary<string, (string Path, bool Temporary)> _nupkgs = new(StringComparer.OrdinalIgnoreCase);
    public List<string> OpenedNupkgs { get; } = [];
    public HashSet<string> TooLarge { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Serves <paramref name="path"/> as the nupkg of id/version; <paramref name="temporary"/> mimics a download.</summary>
    public FakeFeed WithNupkg(string id, string version, string path, bool temporary = false)
    {
        _nupkgs[$"{id}/{NuGetVersion.Parse(version).ToNormalizedString()}"] = (path, temporary);
        return this;
    }

    public Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct)
    {
        var key = $"{id}/{v.ToNormalizedString()}";
        OpenedNupkgs.Add(key);
        if (TooLarge.Contains(key)) throw new NupkgTooLargeException(id, v, 1);
        return Task.FromResult(_nupkgs.TryGetValue(key, out var n)
            ? new NupkgHandle(n.Path, n.Temporary, new FileInfo(n.Path).Length)
            : null);
    }
}
