namespace PackageCheck.Report;

/// <summary>The report JSON Schema shipped inside the tool (design 7.6), identical to <c>schema/report.schema.json</c>.</summary>
public static class ReportSchema
{
    public static string Read()
    {
        using var stream = typeof(ReportSchema).Assembly.GetManifestResourceStream("report.schema.json")
            ?? throw new InvalidOperationException("report.schema.json is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
