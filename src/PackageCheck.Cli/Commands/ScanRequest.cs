using PackageCheck.Core;
using PackageCheck.Core.Model;

namespace PackageCheck.Cli.Commands;

/// <summary>One <c>scan</c> run, as parsed from the command line (design 6.1).</summary>
public sealed record ScanRequest(
    IReadOnlyList<string> Paths,
    string Target,
    FailOn FailOn,
    bool Transitive,
    bool UseMsBuild,
    ScanOptions Options,
    bool Offline,
    string? OverridesPath,
    string? PrivateIdsPath,
    TimeSpan MaxAge,
    bool NoCache,
    string? CacheDir = null);
