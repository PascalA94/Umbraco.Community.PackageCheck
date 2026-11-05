using System.Globalization;
using NuGet.Versioning;
using PackageCheck.Core;
using PackageCheck.Core.Overrides;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PackageCheck.Sources.Overrides;

/// <summary>
/// Reads override files in the design 7.8 entry format, as YAML or JSON (one parser: YAML is a superset of JSON).
/// Accepts one package (<c>id</c> + <c>entries</c>) or a list (<c>overrides: [...]</c>, the seed and release shape).
/// An invalid entry is skipped with a warning; an unreadable file is a usage error (exit 2).
/// </summary>
public static class OverrideFileParser
{
    /// <summary>Override files are small curated lists; the cap keeps a hostile URL from exhausting memory.</summary>
    public const int MaxBytes = 1024 * 1024;

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static OverrideSet Parse(string content, string fileNameOrUrl, OverrideOrigin origin, ICollection<string>? warnings = null)
    {
        if (content.Length > MaxBytes) throw new UsageException($"overrides file {fileNameOrUrl} is larger than {MaxBytes} bytes");
        FileDto? file;
        try
        {
            file = Yaml.Deserialize<FileDto?>(content);
        }
        catch (YamlException e)
        {
            // Position only: the parser's message can quote file content.
            throw new UsageException($"could not read overrides file {fileNameOrUrl} (line {e.Start.Line}, column {e.Start.Column})");
        }

        var entries = new List<OverrideEntry>();
        var packages = file is null ? [] : file.Overrides ?? [new PackageDto { Id = file.Id, Entries = file.Entries }];
        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id))
            {
                warnings?.Add($"{fileNameOrUrl}: an overrides block has no id; skipped");
                continue;
            }
            foreach (var (dto, i) in (package.Entries ?? []).Select((e, i) => (e, i)))
            {
                var entry = ToEntry(package.Id.Trim(), dto, origin, out var problem);
                if (entry is null) warnings?.Add($"{fileNameOrUrl}: {package.Id} entry {i + 1} skipped: {problem}");
                else entries.Add(entry);
            }
        }
        return new OverrideSet(entries, fileNameOrUrl);
    }

    private static OverrideEntry? ToEntry(string id, EntryDto e, OverrideOrigin origin, out string problem)
    {
        problem = "";
        if (!VersionRange.TryParse(e.PackageVersions ?? "", out var versions)) { problem = "packageVersions is not a NuGet range"; return null; }
        if (!VersionRange.TryParse(e.Umbraco ?? "", out var umbraco)) { problem = "umbraco is not a NuGet range"; return null; }
        OverrideStatus? status = e.Status?.Trim().ToLowerInvariant() switch
        {
            "works" => OverrideStatus.Works,
            "broken" => OverrideStatus.Broken,
            "partial" => OverrideStatus.Partial,
            _ => null,
        };
        if (status is null) { problem = "status must be works, broken or partial"; return null; }
        if (string.IsNullOrWhiteSpace(e.Summary)) { problem = "summary is required"; return null; }
        // Governance (design 7.8): every entry needs a public evidence link, and only https is shown as a link.
        if (!Uri.TryCreate(e.Evidence, UriKind.Absolute, out var evidence) || evidence.Scheme != Uri.UriSchemeHttps)
        {
            problem = "evidence must be an absolute https URL";
            return null;
        }
        if (!TryDate(e.VerifiedOn, out var verifiedOn)) { problem = "verifiedOn must be a yyyy-MM-dd date"; return null; }
        var reviewBy = verifiedOn.AddMonths(12);
        if (e.ReviewBy is not null && !TryDate(e.ReviewBy, out reviewBy)) { problem = "reviewBy must be a yyyy-MM-dd date"; return null; }

        return new OverrideEntry(id, versions, umbraco, status.Value, e.Summary.Trim(), evidence,
            string.IsNullOrWhiteSpace(e.ReportedBy) ? null : e.ReportedBy.Trim(), verifiedOn, reviewBy,
            string.IsNullOrWhiteSpace(e.Replacement) ? null : e.Replacement.Trim(), origin);
    }

    private static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // Mutable DTOs for YamlDotNet; everything is a string so validation and messages stay in one place.
    // Two shapes, deliberately not self-referencing, so a nested "overrides" inside a package is ignored, not walked.
    private sealed class FileDto
    {
        public string? Id { get; set; }
        public List<EntryDto>? Entries { get; set; }
        public List<PackageDto>? Overrides { get; set; }
    }

    private sealed class PackageDto
    {
        public string? Id { get; set; }
        public List<EntryDto>? Entries { get; set; }
    }

    private sealed class EntryDto
    {
        public string? PackageVersions { get; set; }
        public string? Umbraco { get; set; }
        public string? Status { get; set; }
        public string? Summary { get; set; }
        public string? Evidence { get; set; }
        public string? ReportedBy { get; set; }
        public string? VerifiedOn { get; set; }
        public string? ReviewBy { get; set; }
        public string? Replacement { get; set; }
    }
}
