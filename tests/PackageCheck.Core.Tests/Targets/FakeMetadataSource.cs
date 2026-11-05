using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Core.Tests.Targets;

/// <summary>Serves Umbraco.Cms versions from memory; <see cref="Offline"/> makes every call fail as a dead feed would.</summary>
internal sealed class FakeMetadataSource : IPackageMetadataSource
{
    private readonly Dictionary<NuGetVersion, string?> _versions = new();
    private readonly HashSet<NuGetVersion> _unlisted = [];
    public bool Offline { get; init; }
    public int Calls { get; private set; }

    public FakeMetadataSource Add(string version, string? tfm = "net10.0", bool listed = true)
    {
        _versions[NuGetVersion.Parse(version)] = tfm;
        if (!listed) _unlisted.Add(NuGetVersion.Parse(version));
        return this;
    }

    public Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct)
    {
        Calls++;
        if (Offline) throw new PackageSourceException(UnknownReason.Offline, "offline");
        return Task.FromResult(new PackageVersions(id, _versions.Keys.Order().ToList()));
    }

    public Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct)
    {
        Calls++;
        if (Offline) throw new PackageSourceException(UnknownReason.Offline, "offline");
        if (!_versions.TryGetValue(v, out var tfm)) return Task.FromResult<PackageDetails?>(null);
        var groups = tfm is null
            ? new List<PackageDependencyGroup>()
            : [new PackageDependencyGroup(NuGetFramework.Parse(tfm), [])];
        return Task.FromResult<PackageDetails?>(new PackageDetails(new PackageIdentity(id, v), groups, null, null, !_unlisted.Contains(v)));
    }

    public Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct) =>
        throw new NotSupportedException("target resolution never opens a nupkg");
}
