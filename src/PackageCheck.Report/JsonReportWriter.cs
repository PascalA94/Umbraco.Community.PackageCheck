using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PackageCheck.Report.Model;

namespace PackageCheck.Report;

/// <summary>Writes <see cref="ScanReport"/> as schema v1 JSON (design 7.6) with source-generated, reflection-free serialisation.</summary>
public static class JsonReportWriter
{
    public static void Write(ScanReport r, Stream s) =>
        JsonSerializer.Serialize(s, r, ReportJsonContext.Default.ScanReport);
}

/// <summary>Writes <c>2026-12-01T10:00:00Z</c> as in design 7.6, rather than STJ's default <c>+00:00</c> offset.</summary>
internal sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}

// Nulls are written, not omitted: consumers key on the field being present (confidence null means "not applicable").
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = true,
    Converters = [typeof(UtcInstantConverter)])]
[JsonSerializable(typeof(ScanReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;
