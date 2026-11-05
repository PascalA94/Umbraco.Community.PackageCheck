using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using PackageCheck.Core;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Core.Overrides;
using PackageCheck.Core.Targets;
using PackageCheck.Report;
using PackageCheck.Report.Model;

namespace PackageCheck.Cli.Commands;

/// <summary>
/// <c>explain &lt;packageId&gt; [version] --target &lt;v&gt;</c> (design 6.1): one package judged as <c>scan</c> would,
/// with every evidence line. Feeds come from the NuGet.config nearest the working directory. There is no site, so
/// <c>current</c> and <c>next-major</c> are usage errors.
/// </summary>
public static class ExplainCommand
{
    public static Command Create(CliEnvironment env)
    {
        var id = new Argument<string>("packageId") { Description = "the NuGet package id" };
        var version = new Argument<string?>("version") { Arity = ArgumentArity.ZeroOrOne, Description = "defaults to the latest stable version" };
        var target = new Option<string>("--target") { Required = true, Description = "17 | 17.7.2 | latest-lts" };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "console", Description = "console | json | md" };
        format.AcceptOnlyFromAmong("console", "json", "md");
        var output = new Option<string?>("--output", "-o") { Description = "write the json or md report to a file" };
        var offline = new Option<bool>("--offline") { Description = "cache, global packages folder and bundled data only" };
        var prerelease = new Option<bool>("--prerelease") { Description = "allow prerelease versions" };
        var noInspect = new Option<bool>("--no-inspect") { Description = "metadata only; no nupkg download or inspection" };
        var overrides = new Option<string?>("--overrides") { Description = "extra overrides file or URL" };
        var cacheDir = new Option<string?>("--cache-dir") { Description = "cache folder" };
        var noCache = new Option<bool>("--no-cache") { Description = "do not read or write the cache" };
        var maxAge = new Option<string>("--max-age") { DefaultValueFactory = _ => "6h", Description = "how long feed metadata is cached" };

        var cmd = new Command("explain", "Show every piece of evidence behind one package's verdict.");
        cmd.Arguments.Add(id);
        cmd.Arguments.Add(version);
        foreach (var o in new Option[] { target, format, output, offline, prerelease, noInspect, overrides, cacheDir, noCache, maxAge })
            cmd.Options.Add(o);

        cmd.SetAction(async (parse, ct) =>
        {
            var requested = parse.GetValue(target)!.Trim();
            if (requested is "current" or "next-major")
                throw new UsageException("--target current and next-major need a site; explain takes a major, an exact version or latest-lts");
            var fmt = parse.GetValue(format)!;
            var outFile = parse.GetValue(output);
            ReportOutput.Validate(fmt, outFile);

            var options = new ScanOptions(Inspect: !parse.GetValue(noInspect), AllowPrerelease: parse.GetValue(prerelease), Verbose: true);
            var request = new ScanRequest([], requested, FailOn.None, false, false, options, parse.GetValue(offline),
                parse.GetValue(overrides), null, DurationParser.Parse(parse.GetValue(maxAge)!), parse.GetValue(noCache), parse.GetValue(cacheDir));

            using var provider = ServiceRegistration.Build(env, request);
            var overrideSet = await provider.GetRequiredService<IOverrideSource>().LoadAsync(ct);
            var services = await provider.GetRequiredService<SiteServicesFactory>().ForAsync(env.WorkingDirectory, overrideSet);

            TargetVersion resolved;
            NuGetVersion pkgVersion;
            var packageId = parse.GetValue(id)!;
            try
            {
                resolved = await services.Resolver.ResolveAsync(requested, null, options.AllowPrerelease, ct);
                pkgVersion = parse.GetValue(version) is { } text
                    ? NuGetVersion.TryParse(text, out var v) ? v : throw new UsageException($"'{text}' is not a version")
                    : LatestStable((await services.Metadata.GetVersionsAsync(packageId, ct)).Versions, options.AllowPrerelease)
                      ?? throw new UsageException($"{packageId} has no stable version; pass a version or --prerelease");
            }
            catch (Exception e) when (e is TargetUnavailableException or PackageSourceException)
            {
                // The message is built from redacted feed URLs only. Exit 3 is for a failed source (design 7.7); an
                // offline miss, a refused private feed or an unknown id is a plain error (review M4 F5).
                var reason = e is TargetUnavailableException t ? t.Reason : ((PackageSourceException)e).Reason;
                await env.Stderr.WriteLineAsync($"error: {TextSafety.Plain(e.Message)}");
                return reason == UnknownReason.SourceError ? ExitCodes.Incomplete : ExitCodes.Usage;
            }

            var pkg = new SitePackage(new PackageRef(packageId, pkgVersion.ToNormalizedString(), pkgVersion, Direct: true, UnresolvedReason: null), []);
            var verdict = await services.Engine.EvaluateAsync(pkg, resolved, options, ct);
            var report = ReportBuilder.Build(
                [new SiteResult(new ScanInput("explain", [], [], TransitiveStatus.NotRequested), resolved, [verdict], Incomplete: false)],
                provider.GetRequiredService<RunWarnings>().Items,
                new ReportOptions(FailOn.None, request.Offline, false, options.Inspect, options.AllowPrerelease),
                env.Clock);

            await ReportOutput.WriteAsync(env, report, fmt, outFile, verbose: true, ct);
            return ExitCodes.Ok;
        });
        return cmd;
    }

    /// <summary>The highest listed stable version, or the highest prerelease when only those exist and they are allowed.</summary>
    public static NuGetVersion? LatestStable(IReadOnlyList<NuGetVersion> versions, bool allowPrerelease) =>
        versions.Where(v => !v.IsPrerelease).Max() ?? (allowPrerelease ? versions.Max() : null);
}
