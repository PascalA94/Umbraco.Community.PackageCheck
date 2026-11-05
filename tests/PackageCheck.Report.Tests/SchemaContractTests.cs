using System.Text.Json;
using Json.Schema;
using static PackageCheck.Report.Tests.ReportSamples;

namespace PackageCheck.Report.Tests;

public class SchemaContractTests
{
    private static readonly string RepoSchemaPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema", "report.schema.json");

    // JsonSchema.Net registers a schema globally by its $id, so it is built once per test run.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(ReportSchema.Read()));

    [Fact]
    public void Schema_IsEmbedded()
    {
        var embedded = ReportSchema.Read();

        Assert.Equal(File.ReadAllText(RepoSchemaPath), embedded);
        using var doc = JsonDocument.Parse(embedded);
        Assert.Equal(
            "https://raw.githubusercontent.com/PascalA94/Umbraco.Community.PackageCheck/main/schema/report.schema.json",
            doc.RootElement.GetProperty("$id").GetString());
    }

    public static TheoryData<string> Reports() => new() { "every-value", "single-site", "empty" };

    [Theory]
    [MemberData(nameof(Reports))]
    public void Json_ValidatesAgainstSchema(string name)
    {
        var report = name switch
        {
            "every-value" => EveryValue(),
            "single-site" => Build([Site(Verdict("A", Core.Model.Verdict.Ok))]),
            _ => Build([]),
        };
        var schema = Schema.Value;

        using var doc = JsonDocument.Parse(JsonReportWriterTests.Json(report));
        var result = schema.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, string.Join("\n", (result.Details ?? []).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))));
    }

    [Fact]
    public void Json_EngineEvidenceIds_Validate()
    {
        // The engine writes "source" and "inspect" evidence for unknowns and inspection notes, not only H01-H19.
        var evidence = new[] { "source", "inspect", "H16" }
            .Select(c => new Core.Heuristics.Evidence(c, Core.Heuristics.EvidenceResult.Info, Core.Heuristics.EvidenceWeight.Info, "tool", "x"))
            .ToList();
        var report = Build([Site(Verdict("A", Core.Model.Verdict.Unknown, reason: Core.Model.UnknownReason.SourceError, evidence: evidence))]);

        using var doc = JsonDocument.Parse(JsonReportWriterTests.Json(report));

        Assert.True(Schema.Value.Evaluate(doc.RootElement).IsValid);
    }

    [Fact]
    public void Schema_RejectsUnknownVerdict()
    {
        var schema = Schema.Value;
        var json = JsonReportWriterTests.Json(Build([Site(Verdict("A", Core.Model.Verdict.Ok))])).Replace("\"ok\"", "\"broken\"");

        using var doc = JsonDocument.Parse(json);

        Assert.False(schema.Evaluate(doc.RootElement).IsValid);
    }
}
