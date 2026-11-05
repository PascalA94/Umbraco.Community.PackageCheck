using System.Runtime.CompilerServices;

namespace PackageCheck.Report.Tests;

/// <summary>
/// Minimal approved-file snapshots: compares output with <c>Snapshots/&lt;Class&gt;.&lt;Test&gt;.verified.&lt;ext&gt;</c>
/// and, on a difference or a missing file, writes <c>.received.&lt;ext&gt;</c> beside it and fails. Accept by renaming
/// after review. Used instead of Verify, whose current releases need a sponsorship declaration in the build.
/// </summary>
internal static class Snapshot
{
    public static void Match(string actual, string extension, [CallerFilePath] string sourceFile = "", [CallerMemberName] string test = "")
    {
        var dir = Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots");
        var stem = $"{Path.GetFileNameWithoutExtension(sourceFile)}.{test}";
        var verified = Path.Combine(dir, $"{stem}.verified.{extension}");
        var received = Path.Combine(dir, $"{stem}.received.{extension}");
        var normalised = actual.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (File.Exists(verified) && File.ReadAllText(verified).Replace("\r\n", "\n", StringComparison.Ordinal) == normalised)
        {
            File.Delete(received);
            return;
        }

        Directory.CreateDirectory(dir);
        File.WriteAllText(received, normalised);
        Assert.Fail(File.Exists(verified)
            ? $"Snapshot differs: compare {received} with {verified}"
            : $"No approved snapshot: review {received} and rename it to {Path.GetFileName(verified)}");
    }
}
