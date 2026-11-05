using Microsoft.Extensions.DependencyInjection;
using PackageCheck.Cli;
using Spectre.Console.Testing;

namespace PackageCheck.Tests.Support;

public sealed record CliResult(int Code, string Stdout, string Stderr, string Console);

/// <summary>Runs the CLI in-process with recorded HTTP, a fixed clock and a throwaway cache (preflight 1.1).</summary>
public static class CliHarness
{
    public static readonly DateTimeOffset RecordedOn = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    public static async Task<CliResult> RunWithAsync(HttpMessageHandler? handler, string? cacheRoot, Action<IServiceCollection>? configure, params string[] args) =>
        await RunInAsync(null, handler, cacheRoot, configure, args);

    public static async Task<CliResult> RunInAsync(string? workingDirectory, HttpMessageHandler? handler, string? cacheRoot, Action<IServiceCollection>? configure, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var console = new TestConsole().Width(250);
        var env = new CliEnvironment(stdout, stderr, console, handler, new FixedNowTimeProvider(RecordedOn),
            cacheRoot ?? Directory.CreateTempSubdirectory("pc-cli-cache-").FullName)
        {
            ConfigureServices = configure,
            GetEnvironmentVariable = _ => null,
            WorkingDirectory = workingDirectory ?? Directory.CreateTempSubdirectory("pc-cli-cwd-").FullName,
        };
        var code = await CliApp.RunAsync(args, env);
        return new CliResult(code, stdout.ToString(), stderr.ToString(), console.Output);
    }

    public static Task<CliResult> RunAsync(HttpMessageHandler? handler, params string[] args) => RunWithAsync(handler, null, null, args);
}

/// <summary>
/// A fixed "now" (cache ages, override review dates) with real timers, so the HTTP policy's retry waits and timeouts
/// still elapse in tests that make a feed fail; FakeTimeProvider timers never fire unless a test advances them.
/// </summary>
public sealed class FixedNowTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
