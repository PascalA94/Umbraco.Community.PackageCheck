using System.Globalization;
using NuGet.Frameworks;
using NuGet.Versioning;
using PackageCheck.Core.Facts;

namespace PackageCheck.Core.Targets;

/// <summary>
/// Turns <c>--target</c> into a <see cref="TargetVersion"/> (design 6.1). <c>current</c> and <c>next-major</c> need the
/// site's version, so the caller resolves once per site (Task 28a). Input failures are a <see cref="UsageException"/>
/// (exit 2); a feed that does not answer is a <see cref="TargetUnavailableException"/> (Task 28b).
/// </summary>
public sealed class TargetResolver(IPackageMetadataSource source, IUmbracoVersionsSource versions, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<TargetVersion> ResolveAsync(string requested, NuGetVersion? siteVersion, bool allowPrerelease, CancellationToken ct)
    {
        var text = requested.Trim();
        switch (text)
        {
            case "current":
                // The site's own version, prerelease or not: CI judges what the site actually runs.
                return await ExactAsync(requested, RequireSite(siteVersion, text), ct);
            case "next-major":
                return await LatestInMajorAsync(requested, RequireSite(siteVersion, text).Major + 1, allowPrerelease, ct);
            case "latest-lts":
                var table = await versions.GetTableAsync(ct);
                return await LatestInMajorAsync(requested, table.LatestLtsMajor(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime)), allowPrerelease, ct);
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
            return await LatestInMajorAsync(requested, major, allowPrerelease, ct);
        if (NuGetVersion.TryParse(text, out var exact))
            return await ExactAsync(requested, exact, ct);

        throw new UsageException($"--target '{requested}' is not a major, a version, current, next-major or latest-lts");
    }

    private static NuGetVersion RequireSite(NuGetVersion? siteVersion, string keyword) =>
        siteVersion ?? throw new UsageException($"--target {keyword} needs the site's Umbraco.Cms version, and none was found");

    private async Task<TargetVersion> LatestInMajorAsync(string requested, int major, bool allowPrerelease, CancellationToken ct)
    {
        PackageVersions all;
        try
        {
            all = await source.GetVersionsAsync(UmbracoFacts.CmsPackageId, ct);
        }
        catch (PackageSourceException e)
        {
            // Without the feed there is no patch list to pick from; an exact version needs no network (ruling R4).
            throw new TargetUnavailableException(e.Reason,
                $"cannot resolve --target {requested} to an Umbraco.Cms {major}.x version ({e.Message}); pass an exact version");
        }

        // Pulled (unlisted) versions are what NuGet's "latest" skips too; a target must be installable.
        var inMajor = new List<NuGetVersion>();
        foreach (var v in all.Versions.Where(v => v.Major == major).OrderDescending())
        {
            if (!await IsListedAsync(v, ct)) continue;
            inMajor.Add(v);
            if (!v.IsPrerelease) break; // the newest listed stable wins; no need to look further down
        }
        var stable = inMajor.FirstOrDefault(v => !v.IsPrerelease);
        if (stable is not null) return await WithTfmAsync(requested, stable, ct);
        if (inMajor.Count == 0) throw new UsageException($"no {major}.x release of Umbraco.Cms on the feed");
        if (!allowPrerelease) throw new UsageException($"no stable {major}.x yet; pass --prerelease or an exact version");
        return await WithTfmAsync(requested, inMajor[0], ct);
    }

    private async Task<bool> IsListedAsync(NuGetVersion v, CancellationToken ct)
    {
        try
        {
            return (await source.GetDetailsAsync(UmbracoFacts.CmsPackageId, v, ct))?.Listed ?? false;
        }
        catch (PackageSourceException)
        {
            // The version list came back, so the feed is up; a details hiccup should not drop a real release.
            return true;
        }
    }

    private async Task<TargetVersion> ExactAsync(string requested, NuGetVersion version, CancellationToken ct) =>
        await WithTfmAsync(requested, version, ct, mustExist: true);

    private async Task<TargetVersion> WithTfmAsync(string requested, NuGetVersion version, CancellationToken ct, bool mustExist = false)
    {
        NuGetFramework? tfm = null;
        try
        {
            var details = await source.GetDetailsAsync(UmbracoFacts.CmsPackageId, version, ct);
            if (details is null && mustExist)
                throw new UsageException($"Umbraco.Cms {version.ToNormalizedString()} is not on the feed");
            // The nuspec is authoritative; even a prerelease's own groups beat the table (design 6.1).
            tfm = details?.DependencyGroups
                .Select(g => g.TargetFramework)
                .Where(f => f.Framework == FrameworkConstants.FrameworkIdentifiers.NetCoreApp)
                .MaxBy(f => f.Version);
        }
        catch (PackageSourceException)
        {
            // Offline or a dead feed: the bundled table is the fallback, so an exact target still works (ruling R4).
        }

        tfm ??= (await versions.GetTableAsync(ct)).FallbackTfm(version.Major)
                ?? throw new UsageException($"no .NET target framework known for Umbraco {version.Major}; check umbraco-versions.json");
        return new TargetVersion(requested, version, tfm, version.IsPrerelease);
    }
}
