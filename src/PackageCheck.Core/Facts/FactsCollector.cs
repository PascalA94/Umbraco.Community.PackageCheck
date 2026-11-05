using System.Collections.Concurrent;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Model;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Facts;

/// <summary>
/// Gathers <see cref="PackageFacts"/> for one package at one target: details, versions, the H04 walk to
/// <c>Umbraco.Cms.*</c> and, when asked, the contents of the package and its dependencies. One instance per run; feed
/// answers and inspections are cached per id and per (id, version).
/// Source failures and unsafe archives surface as <see cref="PackageSourceException"/> for the engine to turn into <c>unknown</c>.
/// </summary>
public sealed class FactsCollector(IPackageMetadataSource meta, IMarketplaceSource? marketplace = null, INupkgInspector? inspector = null,
    IProjectFileSystem? projectFs = null)
{
    /// <summary>A direct dependency is depth 1; <c>Umbraco.Cms.*</c> may sit at depth 3 at most (design 7.4 H04, R3).</summary>
    public const int MaxDepth = 3;

    // Guards the walk against a feed whose lowest versions are all unlisted; past this the lowest is used as NuGet would.
    private const int MaxUnlistedSkips = 10;

    private readonly ConcurrentDictionary<string, Lazy<Task<PackageVersions?>>> _versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<PackageDetails?>>> _details = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<Inspected>>> _inspections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One nupkg's contents, or null with a note when it could not be read (over the size cap, not on the feed).</summary>
    private sealed record Inspected(InspectionResult? Result, string? Note);

    public async Task<PackageFacts> CollectAsync(string id, NuGetVersion v, TargetVersion t, FactsOptions o, CancellationToken ct)
    {
        var all = await VersionsAsync(id, ct)
                  ?? throw new PackageSourceException(UnknownReason.NotFound, $"{id} was not found on any feed");
        var details = await DetailsAsync(id, v, ct)
                      ?? throw new PackageSourceException(UnknownReason.NotFound, $"{id} {v.ToNormalizedString()} was not found on the feed");

        var group = NearestGroup(details, t.Tfm);
        var missing = new List<string>();
        var (umbraco, umbracoDeps) = await WalkAsync(details, t.Tfm, missing, ct);
        var noCompatibleGroup = group is null && details.DependencyGroups.Count > 0;
        // A package with no route to the CMS is not worth a Marketplace call (and is hidden unless --verbose).
        var askMarketplace = marketplace is not null && o.Marketplace && (umbraco.Count > 0 || noCompatibleGroup);

        InspectionResult? own = null;
        List<InspectionResult> deps = [], assetDeps = [];
        var notes = new List<string>();
        if (o.Inspect && inspector is not null)
        {
            var self = await InspectAsync(details.Identity, ct);
            own = self.Result;
            if (self.Note is not null) notes.Add(self.Note);
            // Dependency contents only count next to the package's own (H07/H09 "in this package or a dependency").
            if (own is not null)
            {
                foreach (var dep in umbracoDeps)
                {
                    var r = await InspectAsync(dep, ct);
                    if (r.Result is not null) deps.Add(r.Result); else if (r.Note is not null) notes.Add(r.Note);
                }
                if (o.AssetDependencies) await InspectAssetDependenciesAsync(id, group, umbracoDeps, assetDeps, notes, ct);
            }
            notes.AddRange(new[] { own }.Concat(deps).Concat(assetDeps).OfType<InspectionResult>()
                .SelectMany(r => (r.Notes ?? []).Select(n => $"{r.PackageId} {r.Version}: {n}")));
        }

        return new PackageFacts(
            id, v, details, group?.TargetFramework, umbraco, umbracoDeps,
            group?.Packages.Select(p => p.Id).ToList() ?? [],
            Own: own, Dependencies: deps,
            // The release base, as for every range check: the API's badge logic has no notion of our prerelease rule.
            Marketplace: askMarketplace ? await marketplace!.GetAsync(id, t.RangeVersion, ct) : null,
            o.ProjectDirectories, all.Versions,
            LatestPublished: await LatestPublishedAsync(id, all.Versions, ct),
            MissingDependencies: missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            NoCompatibleGroup: noCompatibleGroup,
            AssetDependencies: assetDeps,
            InspectionNotes: notes,
            ProjectAppPlugins: projectFs is null ? null
                : o.ProjectDirectories.SelectMany(projectFs.AppPluginsFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Ruling R29: direct dependencies with no path to the CMS are inspected too, and kept when they ship client assets or
    /// a manifest. That is the split "UI" package (Plumber.Workflow -> Plumber.Backoffice), whose legacy manifest is the
    /// package's own H08. Only dependencies sharing the package's first id segment are opened: without that, every
    /// library dependency (SkiaSharp native assets, ClosedXML) is downloaded to learn it has no backoffice (Task 24 ruling).
    /// </summary>
    private async Task InspectAssetDependenciesAsync(string id, PackageDependencyGroup? group, IReadOnlyList<PackageIdentity> umbracoDeps,
        List<InspectionResult> assetDeps, List<string> notes, CancellationToken ct)
    {
        var family = id.Split('.')[0] + ".";
        foreach (var d in group?.Packages ?? [])
        {
            if (IsCms(d.Id) || IsPlatform(d.Id) || umbracoDeps.Any(u => u.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase))) continue;
            if (!d.Id.StartsWith(family, StringComparison.OrdinalIgnoreCase)) continue;
            var resolved = await ResolveLowestAsync(d.Id, d.VersionRange, ct);
            if (resolved is null) continue;
            var r = await InspectAsync(resolved.Identity, ct);
            if (r.Result is { } c && (c.HasClientAssets || c.HasUmbracoPackageJson || c.HasLegacyManifest)) assetDeps.Add(c);
            else if (r.Note is not null) notes.Add(r.Note);
        }
    }

    /// <summary>
    /// Opens and inspects one nupkg, once per run. The handle is disposed in a finally, so a temporary download is deleted
    /// whether inspection succeeds or throws (Review Focus 5). An unsafe archive propagates: the package becomes unknown.
    /// </summary>
    private Task<Inspected> InspectAsync(PackageIdentity id, CancellationToken ct) =>
        _inspections.GetOrAdd($"{id.Id}/{id.Version.ToNormalizedString()}", _ => new Lazy<Task<Inspected>>(async () =>
        {
            var name = $"{id.Id} {id.Version.ToNormalizedString()}";
            try
            {
                var handle = await meta.OpenNupkgAsync(id.Id, id.Version, ct);
                if (handle is null) return new Inspected(null, $"{name}: nupkg not available; metadata only");
                await using (handle)
                {
                    return new Inspected(await inspector!.InspectAsync(id, handle, ct), null);
                }
            }
            catch (NupkgTooLargeException)
            {
                return new Inspected(null, $"{name}: nupkg over size cap; metadata only");
            }
        })).Value;

    // Unlisted nuget.org versions carry no real date (PackageDetails.Published), so walk down to the newest that has one.
    private const int MaxPublishedLookups = 5;

    private async Task<DateTimeOffset?> LatestPublishedAsync(string id, IReadOnlyList<NuGetVersion> versions, CancellationToken ct)
    {
        foreach (var v in versions.OrderDescending().Take(MaxPublishedLookups))
            if ((await DetailsAsync(id, v, ct))?.Published is { } published) return published;
        return null;
    }

    /// <summary>
    /// The CMS itself: <c>Umbraco.Cms</c> and <c>Umbraco.Cms.*</c>, except the Integrations add-ons, which share the
    /// prefix but are ordinary packages with their own path to the CMS.
    /// </summary>
    public static bool IsCms(string id) =>
        id.Equals(UmbracoFacts.CmsPackageId, StringComparison.OrdinalIgnoreCase)
        || (id.StartsWith(UmbracoFacts.CmsPackageId + ".", StringComparison.OrdinalIgnoreCase)
            && !id.StartsWith(UmbracoFacts.CmsPackageId + ".Integrations.", StringComparison.OrdinalIgnoreCase));

    private static bool IsPlatform(string id) =>
        id.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("NETStandard", StringComparison.OrdinalIgnoreCase);

    /// <summary>The group NuGet restore would pick for the target; never the union of groups (ruling R21).</summary>
    public static PackageDependencyGroup? NearestGroup(PackageDetails d, NuGetFramework tfm) =>
        NuGetFrameworkUtility.GetNearest(d.DependencyGroups, tfm, g => g.TargetFramework);

    private static IEnumerable<PackageDependency> Deps(PackageDetails d, NuGetFramework tfm) => NearestGroup(d, tfm)?.Packages ?? [];

    private async Task<(List<UmbracoDependency>, List<PackageIdentity>)> WalkAsync(PackageDetails root, NuGetFramework tfm, List<string> missing, CancellationToken ct)
    {
        var rootId = root.Identity.Id;
        var umbraco = Deps(root, tfm).Where(d => IsCms(d.Id))
            .Select(d => new UmbracoDependency(rootId, d.Id, d.VersionRange, 1)).ToList();
        // Direct ranges are the package's own statement; deeper ranges only stand in when there is none (design 7.4 H04).
        var walkForRanges = umbraco.Count == 0;
        var umbracoDeps = new List<PackageIdentity>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        var frontier = new List<(PackageDetails Pkg, int Depth)> { (root, 0) };
        while (frontier.Count > 0)
        {
            var next = new List<(PackageDetails, int)>();
            foreach (var (pkg, depth) in frontier)
            {
                foreach (var d in Deps(pkg, tfm))
                {
                    if (IsCms(d.Id))
                    {
                        if (walkForRanges && depth > 0) umbraco.Add(new UmbracoDependency(pkg.Identity.Id, d.Id, d.VersionRange, depth + 1));
                        continue;
                    }
                    if (IsPlatform(d.Id) || !seen.Add(d.Id)) continue;
                    // The resolved package sits at depth + 1, so its own Umbraco.Cms.* dependency may use what is left (R34).
                    var remaining = MaxDepth - (depth + 1);
                    if (remaining < 1) continue;
                    var resolved = await ResolveLowestAsync(d.Id, d.VersionRange, ct);
                    if (resolved is null) { missing.Add(d.Id); continue; }
                    if (!await ReachesCmsAsync(resolved, tfm, remaining, missing, ct)) continue;
                    umbracoDeps.Add(resolved.Identity);
                    next.Add((resolved, depth + 1));
                }
            }
            frontier = next;
        }
        return (umbraco, umbracoDeps);
    }

    private async Task<bool> ReachesCmsAsync(PackageDetails p, NuGetFramework tfm, int hops, List<string> missing, CancellationToken ct)
    {
        var deps = Deps(p, tfm).ToList();
        if (deps.Any(d => IsCms(d.Id))) return true;
        if (hops <= 1) return false;
        foreach (var d in deps.Where(d => !IsPlatform(d.Id)))
        {
            var r = await ResolveLowestAsync(d.Id, d.VersionRange, ct);
            if (r is null) missing.Add(d.Id);
            else if (await ReachesCmsAsync(r, tfm, hops - 1, missing, ct)) return true;
        }
        return false;
    }

    /// <summary>NuGet's lowest-applicable rule, preferring listed versions; prereleases only when the floor is one.</summary>
    private async Task<PackageDetails?> ResolveLowestAsync(string id, VersionRange range, CancellationToken ct)
    {
        var versions = await VersionsAsync(id, ct);
        if (versions is null) return null;
        var allowPre = range.MinVersion?.IsPrerelease ?? false;
        PackageDetails? first = null;
        var tried = 0;
        foreach (var v in versions.Versions.Where(v => range.Satisfies(v) && (allowPre || !v.IsPrerelease)))
        {
            var d = await DetailsAsync(id, v, ct);
            if (d is null) continue;
            if (d.Listed) return d;
            first ??= d;
            if (++tried >= MaxUnlistedSkips) break;
        }
        return first;
    }

    /// <summary>Null when no feed has the id; other source failures propagate so the verdict is unknown, not a guess.</summary>
    private Task<PackageVersions?> VersionsAsync(string id, CancellationToken ct) =>
        _versions.GetOrAdd(id, key => new Lazy<Task<PackageVersions?>>(async () =>
        {
            try { return await meta.GetVersionsAsync(key, ct); }
            catch (PackageSourceException e) when (e.Reason == UnknownReason.NotFound) { return null; }
        })).Value;

    private Task<PackageDetails?> DetailsAsync(string id, NuGetVersion v, CancellationToken ct) =>
        _details.GetOrAdd($"{id}/{v.ToNormalizedString()}", _ => new Lazy<Task<PackageDetails?>>(() => meta.GetDetailsAsync(id, v, ct))).Value;
}
