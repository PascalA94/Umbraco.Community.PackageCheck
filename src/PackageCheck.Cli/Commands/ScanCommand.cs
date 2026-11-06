using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PackageCheck.Core;
using PackageCheck.Core.Model;
using PackageCheck.Report;

namespace PackageCheck.Cli.Commands;

/// <summary><c>scan [paths...]</c> with the options of design 6.1, plus <c>--private-ids</c> (design 7.3).</summary>
public static class ScanCommand
{
    public static Command Create(CliEnvironment env)
    {
        var paths = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore, Description = ".sln, .slnx, .csproj or folder; each is one site" };
        var target = new Option<string>("--target") { DefaultValueFactory = _ => "next-major", Description = "17 | 17.7.2 | current | next-major | latest-lts" };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "console", Description = "console | json | md" };
        format.AcceptOnlyFromAmong("console", "json", "md");
        var output = new Option<string?>("--output", "-o") { Description = "write the json or md report to a file; the console summary is still printed" };
        var transitive = new Option<bool>("--transitive") { Description = "include transitive Umbraco packages (lock file or --msbuild)" };
        var msbuild = new Option<bool>("--msbuild") { Description = "exact versions via dotnet list package (needs restore)" };
        var prerelease = new Option<bool>("--prerelease") { Description = "allow prerelease versions as an upgrade target" };
        var noInspect = new Option<bool>("--no-inspect") { Description = "metadata only; no nupkg download or inspection" };
        var offline = new Option<bool>("--offline") { Description = "cache, global packages folder and bundled data only" };
        var overrides = new Option<string?>("--overrides") { Description = "extra overrides file or URL on top of the community ones" };
        var privateIds = new Option<string?>("--private-ids") { Description = "file of package ids on a private feed; never looked up" };
        var cacheDir = new Option<string?>("--cache-dir") { Description = "cache folder" };
        var maxAge = new Option<string>("--max-age") { DefaultValueFactory = _ => "6h", Description = "how long feed metadata is cached: 30m, 6h, 1d" };
        var noCache = new Option<bool>("--no-cache") { Description = "do not read or write the cache" };
        var failOn = new Option<string>("--fail-on") { DefaultValueFactory = _ => "none", Description = "none | suspicious | upgrade | no-release" };
        var verbose = new Option<bool>("--verbose") { Description = "print every piece of evidence" };

        var cmd = new Command("scan", "Check every package on one or more Umbraco sites against a target version.");
        cmd.Arguments.Add(paths);
        foreach (var o in new Option[] { target, format, output, failOn, transitive, msbuild, prerelease, noInspect, offline, overrides, privateIds, cacheDir, maxAge, noCache, verbose })
            cmd.Options.Add(o);

        cmd.SetAction(async (parse, ct) =>
        {
            var fmt = parse.GetValue(format)!;
            var outFile = parse.GetValue(output);
            ReportOutput.Validate(fmt, outFile);
            var isVerbose = parse.GetValue(verbose);

            var request = new ScanRequest(
                (parse.GetValue(paths) is { Length: > 0 } given ? given : ["."]).Select(p => Path.GetFullPath(p, env.WorkingDirectory)).ToList(),
                parse.GetValue(target)!,
                FailOnPolicy.Parse(parse.GetValue(failOn)!),
                parse.GetValue(transitive),
                parse.GetValue(msbuild),
                new ScanOptions(Inspect: !parse.GetValue(noInspect), AllowPrerelease: parse.GetValue(prerelease), Verbose: isVerbose),
                parse.GetValue(offline),
                parse.GetValue(overrides),
                parse.GetValue(privateIds) is { } ids ? Path.GetFullPath(ids, env.WorkingDirectory) : null,
                DurationParser.Parse(parse.GetValue(maxAge)!),
                parse.GetValue(noCache),
                parse.GetValue(cacheDir));

            using var provider = ServiceRegistration.Build(env, request);
            var report = await provider.GetRequiredService<ScanOrchestrator>().RunAsync(request, ct);

            await ReportOutput.WriteAsync(env, report, fmt, outFile, isVerbose, ct);
            return ExitCodes.Compute(report, request.FailOn);
        });
        return cmd;
    }
}
