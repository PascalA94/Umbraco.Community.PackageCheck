using System.CommandLine;
using PackageCheck.Cli.Commands;
using PackageCheck.Core;
using PackageCheck.Report;

namespace PackageCheck.Cli;

/// <summary>
/// Entry point. Exit codes (design 7.7): 2 for a usage error or an unhandled exception, with one line on stderr and the
/// stack trace only with <c>--verbose</c>; otherwise what the command returns.
/// </summary>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args, CliEnvironment env)
    {
        // System.CommandLine's own --version prints the informational version, which carries "+<sha>" from SourceLink.
        if (args is ["--version"])
        {
            await env.Stdout.WriteLineAsync(ToolInfo.Version);
            return 0;
        }

        var verbose = args.Contains("--verbose", StringComparer.Ordinal);
        try
        {
            var root = new RootCommand("Reports which NuGet packages on an Umbraco site are ready for an Umbraco upgrade.");
            root.Subcommands.Add(ScanCommand.Create(env));
            root.Subcommands.Add(ExplainCommand.Create(env));
            root.Subcommands.Add(CacheCommand.Create(env));
            var parse = root.Parse(args);
            if (parse.Errors.Count > 0)
            {
                foreach (var error in parse.Errors) await env.Stderr.WriteLineAsync($"error: {error.Message}");
                return ExitCodes.Usage;
            }
            return await parse.InvokeAsync(new InvocationConfiguration
            {
                Output = env.Stdout,
                Error = env.Stderr,
                EnableDefaultExceptionHandler = false,
            });
        }
        catch (UsageException e)
        {
            await env.Stderr.WriteLineAsync($"error: {OneLine(e.Message)}");
            return ExitCodes.Usage;
        }
        catch (OperationCanceledException)
        {
            await env.Stderr.WriteLineAsync("cancelled");
            return ExitCodes.Cancelled;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await env.Stderr.WriteLineAsync($"error: unexpected {e.GetType().Name}: {OneLine(e.Message)}" +
                                            (verbose ? Environment.NewLine + e.StackTrace : ""));
            return ExitCodes.Usage;
        }
    }

    /// <summary>
    /// Exception text can quote repo-controlled paths or a library's URL: control characters and userinfo are removed
    /// before it reaches the terminal (review M4 F9, M4-S8).
    /// </summary>
    private static string OneLine(string message) => TextSafety.Plain(message).Trim();
}
