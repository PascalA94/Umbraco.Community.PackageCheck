using PackageCheck.Report.Model;

namespace PackageCheck.Report;

/// <summary>Wording shared by the markdown and console writers, so the two never describe a package differently.</summary>
internal static class ReportText
{
    /// <summary>Verdict groups worst first (design 7.6); upgrade and replaced share a group, as they share a severity (design 6.3).</summary>
    public static readonly (string Title, string[] Verdicts)[] Groups =
    [
        ("No release", ["no-release"]),
        ("Upgrade or replace", ["upgrade", "replaced"]),
        ("Suspicious", ["suspicious"]),
        ("Unknown", ["unknown"]),
        ("OK", ["ok"]),
        ("Not Umbraco", ["not-umbraco"]),
    ];

    public static readonly string[] SummaryOrder = ["no-release", "upgrade", "replaced", "suspicious", "unknown", "ok"];

    public const string Umbraco17CheckUrl = "https://www.nuget.org/packages/Umbraco17Check";

    public static int Rank(string verdict) => Array.FindIndex(Groups, g => g.Verdicts.Contains(verdict));

    /// <summary>
    /// Override status in prose. The JSON keeps the data repo's wire value; the word "broken" never reaches a reader
    /// (ruling R5, preflight C2).
    /// </summary>
    public static string Status(string status) => status switch
    {
        "works" => "reported working",
        "broken" => "reported not working",
        "partial" => "reported partly working",
        _ => "reported",
    };

    public static string Target(TargetReport t) =>
        t.Tfm is null ? $"{t.Requested} (unresolved)" : $"{t.Resolved} ({t.Requested}, {t.Tfm}{(t.Prerelease ? ", prerelease" : "")})";

    public static string Summary(ReportSummary s) =>
        string.Join(", ", SummaryOrder.Select(v => $"{s.Counts.GetValueOrDefault(v)} {v}"));

    /// <summary>
    /// Project file names for the console and markdown tables; the JSON keeps the paths. When two projects share a file
    /// name, the paths are shown so neither disappears (review M4 F3).
    /// </summary>
    public static string Projects(IReadOnlyList<string> paths)
    {
        // Only project files are shortened; anything else (a name already) is shown as given.
        var names = paths.Select(p => Path.GetExtension(p).EndsWith("proj", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(p) : p).ToList();
        return string.Join(", ", names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count ? names : paths);
    }

    public static string Plural(int n, string one, string many) => n == 1 ? $"{n} {one}" : $"{n} {many}";
}
