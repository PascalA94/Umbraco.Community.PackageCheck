using PackageCheck.Core.Targets;

namespace PackageCheck.Sources.Data;

/// <summary>
/// The data snapshot shipped inside the tool (design 7.8): <c>data/umbraco-versions.json</c> and
/// <c>data/overrides.seed.json</c>, embedded so offline runs and <c>latest-lts</c> work without the overrides release.
/// </summary>
public static class BundledData
{
    public const string UmbracoVersionsResource = "PackageCheck.Data.umbraco-versions.json";
    public const string OverridesSeedResource = "PackageCheck.Data.overrides.seed.json";

    public static Stream Open(string resource) =>
        typeof(BundledData).Assembly.GetManifestResourceStream(resource)
        ?? throw new InvalidOperationException($"bundled resource {resource} is missing from the build");
}

/// <summary>The bundled majors table (ruling R13). Task 32 adds the live overrides-release source in front of it.</summary>
public sealed class BundledUmbracoVersionsSource : IUmbracoVersionsSource
{
    private readonly Lazy<UmbracoVersionsTable> _table =
        new(() => { using var s = BundledData.Open(BundledData.UmbracoVersionsResource); return UmbracoVersionsTable.Parse(s); });

    public Task<UmbracoVersionsTable> GetTableAsync(CancellationToken ct) => Task.FromResult(_table.Value);
}
