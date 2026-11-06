using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NuGet.Versioning;
using PackageCheck.Core.Model;

namespace PackageCheck.Inputs;

/// <param name="Output">Standard output.</param>
/// <param name="TimedOut">The process was killed at the timeout; <paramref name="ExitCode"/> is then meaningless.</param>
/// <param name="Error">Standard error, kept apart so it can never corrupt the JSON on standard output.</param>
public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut, string Error = "");

/// <summary>Starts a process with an argument array and no shell, so a path with spaces or quotes stays one argument.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout, CancellationToken ct);
}

/// <summary><c>dotnet list package</c> failed or printed something unusable; the reader falls back to project files.</summary>
public sealed class MsBuildListException(string message) : Exception(message);

/// <summary>
/// <c>--msbuild</c> (design 6.1): exact resolved versions, and the transitive graph, from
/// <c>dotnet list &lt;path&gt; package --format json</c>. It needs a prior restore and never restores itself: newer SDKs
/// restore before listing unless given <c>--no-restore</c>, which would write obj/ files in the scanned folder and break
/// "the tool reports only". SDK 8 rejects that flag (and never restores), so the flag is left out only when
/// <c>dotnet --version</c> in the scanned folder reports 8 or lower; an unknown version keeps it (security review M3).
/// The host is found by <see cref="DotnetHost"/>, never in the current directory (security review M2).
/// </summary>
public sealed class MsBuildListRunner(IProcessRunner runner, Func<string?>? findDotnet = null)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromMinutes(1);
    private const string NoRestore = "--no-restore";
    private readonly Func<string?> _findDotnet = findDotnet ?? (() => DotnetHost.Find(Environment.GetEnvironmentVariable));

    public async Task<IReadOnlyList<PackageRef>> ListAsync(string solutionOrProject, bool includeTransitive, CancellationToken ct)
    {
        var host = _findDotnet() ?? throw new MsBuildListException("dotnet was not found on PATH");

        List<string> Args(bool noRestore)
        {
            var args = new List<string> { "list", solutionOrProject, "package", "--format", "json" };
            if (includeTransitive) args.Add("--include-transitive");
            if (noRestore) args.Add(NoRestore);
            return args;
        }

        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionOrProject)) ?? Environment.CurrentDirectory;
        // The scanned folder's global.json picks the SDK, so ask that SDK, from that folder.
        var version = await runner.RunAsync(host, ["--version"], workingDirectory, VersionTimeout, ct);
        var major = !version.TimedOut && version.ExitCode == 0
                    && int.TryParse(version.Output.Trim().Split('.')[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var m)
            ? m : (int?)null;
        var result = await runner.RunAsync(host, Args(noRestore: major is null or > 8), workingDirectory, Timeout, ct);

        if (result.TimedOut) throw new MsBuildListException($"dotnet list package timed out after {Timeout.TotalMinutes:0} minutes");
        if (result.ExitCode != 0) throw new MsBuildListException($"dotnet list package exited with code {result.ExitCode}");
        return Parse(result.Output);
    }

    private static IReadOnlyList<PackageRef> Parse(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1)
                throw new MsBuildListException("dotnet list package printed an output version this tool does not read");
            if (root.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array && problems.GetArrayLength() > 0)
                throw new MsBuildListException("dotnet list package reported a problem");

            var refs = new List<PackageRef>();
            foreach (var project in Array(root, "projects"))
            {
                // The first framework stands in for the project, as with lock files (ProjectReader).
                var framework = Array(project, "frameworks").FirstOrDefault();
                if (framework.ValueKind != JsonValueKind.Object)
                    throw new MsBuildListException("dotnet list package listed a project with no restored framework");
                refs.AddRange(Array(framework, "topLevelPackages").Select(p => Ref(p, direct: true)));
                refs.AddRange(Array(framework, "transitivePackages").Select(p => Ref(p, direct: false)));
            }
            return refs;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new MsBuildListException("dotnet list package printed output this tool could not read");
        }
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToList() : [];

    private static PackageRef Ref(JsonElement p, bool direct)
    {
        // TryGetProperty: a missing property must fail as FormatException (fallback), never KeyNotFoundException (review F2).
        var id = (p.TryGetProperty("id", out var i) ? i.GetString() : null) ?? throw new FormatException("package without id");
        var resolved = (p.TryGetProperty("resolvedVersion", out var rv) ? rv.GetString() : null)
                       ?? throw new FormatException($"{id} has no resolved version");
        var version = NuGetVersion.Parse(resolved);
        var requested = p.TryGetProperty("requestedVersion", out var r) ? r.GetString() : null;
        return new PackageRef(id, requested ?? resolved, version, direct, null);
    }
}

/// <summary>Runs <c>dotnet</c> without a shell; output is capped and the process tree is killed at the timeout.</summary>
public sealed class DotnetProcessRunner : IProcessRunner
{
    private const int MaxOutputChars = 32 * 1024 * 1024;

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // A first run of an SDK otherwise prints a banner, and can create a developer HTTPS certificate on the machine.
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();
        static void Append(StringBuilder sb, string? line)
        {
            if (line is null) return;
            lock (sb)
            {
                if (sb.Length < MaxOutputChars) sb.AppendLine(line);
            }
        }
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(error, e.Data);
        try
        {
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Security review L2: a host that cannot start is an --msbuild failure (fallback), not a crash.
            throw new MsBuildListException("dotnet could not be started");
        }
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            return new ProcessResult(-1, "", TimedOut: true);
        }
        // The parameterless wait flushes the async output readers.
        process.WaitForExit();
        lock (output) lock (error) return new ProcessResult(process.ExitCode, output.ToString(), TimedOut: false, error.ToString());
    }
}

/// <summary>
/// Finds the <c>dotnet</c> host as an absolute path: <c>DOTNET_HOST_PATH</c> when it is rooted, else the first rooted
/// <c>PATH</c> entry that holds it. Empty, <c>.</c> and other relative entries are skipped, and the current directory is
/// never searched, so a <c>dotnet</c> file committed to a scanned repository cannot run (security review M2).
/// </summary>
public static class DotnetHost
{
    public static string? Find(Func<string, string?> env)
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (env("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && Path.IsPathFullyQualified(hostPath) && File.Exists(hostPath))
            return hostPath;
        foreach (var dir in (env("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(dir)) continue;
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
