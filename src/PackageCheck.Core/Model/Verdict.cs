namespace PackageCheck.Core.Model;

/// <summary>The verdict for one package at one target. Wire values are fixed by the report schema (global constraints).</summary>
public enum Verdict { Ok, Upgrade, NoRelease, Suspicious, Replaced, Unknown, NotUmbraco }

/// <summary>How sure the verdict is; null on the wire (and here) for unknown and not-umbraco.</summary>
public enum Confidence { High, Medium, Low }

public static class VerdictWireValues
{
    public static string ToWireValue(this Verdict v) => v switch
    {
        Verdict.Ok => "ok",
        Verdict.Upgrade => "upgrade",
        Verdict.NoRelease => "no-release",
        Verdict.Suspicious => "suspicious",
        Verdict.Replaced => "replaced",
        Verdict.Unknown => "unknown",
        Verdict.NotUmbraco => "not-umbraco",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToWireValue(this Confidence c) => c switch
    {
        Confidence.High => "high",
        Confidence.Medium => "medium",
        Confidence.Low => "low",
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, null),
    };
}
