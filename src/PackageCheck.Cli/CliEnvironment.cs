using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace PackageCheck.Cli;

/// <summary>
/// Everything the CLI touches outside itself, so tests run it in-process with recorded HTTP, a fixed clock and a
/// throwaway cache. <see cref="Handler"/> null means real HTTP; <see cref="CacheRoot"/> null means the default cache
/// folder (design 7.5). <see cref="ConfigureServices"/> lets a test replace one registration; production leaves it null.
/// </summary>
public sealed record CliEnvironment(
    TextWriter Stdout,
    TextWriter Stderr,
    IAnsiConsole Console,
    HttpMessageHandler? Handler,
    TimeProvider Clock,
    string? CacheRoot)
{
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    /// <summary>Where relative paths resolve and whose NuGet.config <c>explain</c> uses.</summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;
}
