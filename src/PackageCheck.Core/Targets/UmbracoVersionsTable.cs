using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Frameworks;

namespace PackageCheck.Core.Targets;

/// <summary>
/// One row of <c>umbraco-versions.json</c> (design 7.8). <paramref name="Eol"/> is the end-of-life date;
/// <paramref name="Released"/> (an addition for H18) is the major's first release, null when not yet released.
/// </summary>
public sealed record UmbracoMajor(int Major, bool Lts, DateOnly Eol, string Tfm, DateOnly? Released = null);

/// <summary>The bundled or released Umbraco majors table. It feeds <c>latest-lts</c> and the offline TFM fallback.</summary>
public sealed class UmbracoVersionsTable(IReadOnlyList<UmbracoMajor> majors)
{
    public IReadOnlyList<UmbracoMajor> Majors { get; } = majors;

    /// <summary>The highest LTS major still inside its support window on <paramref name="today"/>.</summary>
    public int LatestLtsMajor(DateOnly today) =>
        Majors.Where(m => m.Lts && m.Eol >= today).Select(m => (int?)m.Major).Max()
        ?? throw new UsageException("no supported LTS major in umbraco-versions.json; pass a major or an exact version");

    public UmbracoMajor? Find(int major) => Majors.FirstOrDefault(m => m.Major == major);

    public NuGetFramework? FallbackTfm(int major) =>
        Majors.FirstOrDefault(m => m.Major == major) is { } m ? NuGetFramework.Parse(m.Tfm) : null;

    /// <summary>Reads the <c>umbraco-versions.json</c> shape: <c>{ "majors": [ { "major", "lts", "eol", "tfm" } ] }</c>.</summary>
    public static UmbracoVersionsTable Parse(Stream json)
    {
        var doc = JsonSerializer.Deserialize<VersionsDocument>(json, JsonOptions)
                  ?? throw new InvalidDataException("umbraco-versions.json is empty");
        if (doc.Majors is not { Count: > 0 }) throw new InvalidDataException("umbraco-versions.json has no majors");
        return new UmbracoVersionsTable(doc.Majors
            .Select(m => new UmbracoMajor(m.Major, m.Lts, m.Eol, m.Tfm ?? throw new InvalidDataException($"major {m.Major} has no tfm"), m.Released))
            .OrderBy(m => m.Major)
            .ToList());
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record VersionsDocument([property: JsonPropertyName("majors")] List<MajorRow>? Majors);

    private sealed record MajorRow(int Major, bool Lts, DateOnly Eol, string? Tfm, DateOnly? Released);
}

/// <summary>Where the majors table comes from: the bundled seed in 0.1, the overrides release from Task 32 (ruling R13).</summary>
public interface IUmbracoVersionsSource
{
    Task<UmbracoVersionsTable> GetTableAsync(CancellationToken ct);
}
