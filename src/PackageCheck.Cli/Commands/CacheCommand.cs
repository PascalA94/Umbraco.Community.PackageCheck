using System.CommandLine;
using PackageCheck.Core;
using PackageCheck.Sources.Cache;

namespace PackageCheck.Cli.Commands;

/// <summary><c>cache info|clear [--cache-dir]</c> (design 6.1). Clear deletes only cache files inside the cache root.</summary>
public static class CacheCommand
{
    public static Command Create(CliEnvironment env)
    {
        var action = new Argument<string>("action") { Description = "info | clear" };
        action.AcceptOnlyFromAmong("info", "clear");
        var cacheDir = new Option<string?>("--cache-dir") { Description = "cache folder" };

        var cmd = new Command("cache", "Show or clear the local cache.");
        cmd.Arguments.Add(action);
        cmd.Options.Add(cacheDir);
        cmd.SetAction(async (parse, ct) =>
        {
            var cache = new FileCache(ServiceRegistration.CacheRoot(env, parse.GetValue(cacheDir)), env.Clock);
            if (parse.GetValue(action) == "clear")
            {
                if (Directory.Exists(cache.Root) && !cache.IsCacheFolder)
                    throw new UsageException($"{cache.Root} is not an umbraco-package-check cache folder (no {FileCache.MarkerFileName}); nothing was deleted");
                cache.Clear();
                // NuGet's own HTTP cache for this tool lives under the marked root (ServiceRegistration). A symlinked
                // nuget-http is only unlinked by Directory.Delete, never followed.
                var http = Path.Combine(cache.Root, "nuget-http");
                if (Directory.Exists(http)) Directory.Delete(http, recursive: true);
                await env.Stdout.WriteLineAsync($"cleared: {cache.Root}");
                return ExitCodes.Ok;
            }

            var info = cache.Info();
            await env.Stdout.WriteLineAsync($"root: {info.Root}");
            await env.Stdout.WriteLineAsync($"entries: {info.Entries}");
            await env.Stdout.WriteLineAsync($"size: {info.Bytes} bytes");
            return ExitCodes.Ok;
        });
        return cmd;
    }
}
