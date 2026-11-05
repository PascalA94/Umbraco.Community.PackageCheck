using PackageCheck.Core;
using PackageCheck.Core.Model;
using PackageCheck.Report.Model;

namespace PackageCheck.Cli;

/// <summary>Exit codes (design 7.7).</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Findings = 1;
    public const int Usage = 2;
    public const int Incomplete = 3;

    /// <summary>Ctrl+C: the shell convention (128 + SIGINT).</summary>
    public const int Cancelled = 130;

    private static readonly Dictionary<string, Verdict> ByWire =
        Enum.GetValues<Verdict>().ToDictionary(v => v.ToWireValue(), StringComparer.Ordinal);

    /// <summary>
    /// 3 when <c>--fail-on</c> is set and a required source failed, even if findings also apply (architect's call: an
    /// incomplete run is the bigger problem in CI); else 1 when a package reaches the threshold; else 0.
    /// </summary>
    public static int Compute(ScanReport r, FailOn threshold)
    {
        if (threshold == FailOn.None) return Ok;
        if (r.Summary.Incomplete) return Incomplete;
        return r.Sites.SelectMany(s => s.Packages).Any(p => FailOnPolicy.Exceeds(ByWire[p.Verdict], threshold)) ? Findings : Ok;
    }
}
