using System.Text;
using PackageCheck.Core;
using PackageCheck.Report;
using PackageCheck.Report.Model;

namespace PackageCheck.Cli.Commands;

/// <summary>Where a report goes (design 6.1): console tables, or json/md on stdout or in <c>-o</c> plus the console summary.</summary>
internal static class ReportOutput
{
    public static void Validate(string format, string? outFile)
    {
        if (outFile is not null && format == "console")
            throw new UsageException("--output needs --format json or --format md");
    }

    public static async Task WriteAsync(CliEnvironment env, ScanReport report, string format, string? outFile, bool verbose, CancellationToken ct)
    {
        if (format == "console")
        {
            ConsoleReportWriter.Write(report, env.Console, verbose);
            return;
        }

        var bytes = format == "json" ? Json(report) : Encoding.UTF8.GetBytes(MarkdownReportWriter.Write(report, verbose));
        if (outFile is not null)
        {
            var full = Path.GetFullPath(outFile, env.WorkingDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, bytes, ct);
            ConsoleReportWriter.Write(report, env.Console, verbose: false);
            return;
        }
        // json and md on stdout carry nothing else, so they can be piped.
        await env.Stdout.WriteAsync(Encoding.UTF8.GetString(bytes));
        await env.Stdout.FlushAsync(ct);
    }

    private static byte[] Json(ScanReport report)
    {
        using var ms = new MemoryStream();
        JsonReportWriter.Write(report, ms);
        return ms.ToArray();
    }
}
