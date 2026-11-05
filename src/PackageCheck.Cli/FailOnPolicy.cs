using PackageCheck.Core;
using PackageCheck.Core.Model;

namespace PackageCheck.Cli;

/// <summary>
/// <c>--fail-on</c> (design 6.1): severity <c>no-release</c> &gt; <c>upgrade</c> = <c>replaced</c> &gt; <c>suspicious</c>,
/// and thresholds nest. <c>ok</c>, <c>unknown</c> and <c>not-umbraco</c> never fail a run on their own.
/// </summary>
public static class FailOnPolicy
{
    public static FailOn Parse(string text) => text switch
    {
        "none" => FailOn.None,
        "suspicious" => FailOn.Suspicious,
        "upgrade" => FailOn.Upgrade,
        "no-release" => FailOn.NoRelease,
        _ => throw new UsageException($"--fail-on '{text}' is not one of none, suspicious, upgrade, no-release"),
    };

    public static bool Exceeds(Verdict v, FailOn threshold) => threshold != FailOn.None && Severity(v) >= (int)threshold;

    /// <summary>Same scale as <see cref="FailOn"/>: 1 suspicious, 2 upgrade or replaced, 3 no-release, 0 never fails.</summary>
    private static int Severity(Verdict v) => v switch
    {
        Verdict.Suspicious => (int)FailOn.Suspicious,
        Verdict.Upgrade or Verdict.Replaced => (int)FailOn.Upgrade,
        Verdict.NoRelease => (int)FailOn.NoRelease,
        _ => 0,
    };
}
