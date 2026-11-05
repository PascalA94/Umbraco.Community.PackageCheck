namespace PackageCheck.Core;

/// <summary>
/// The <c>--fail-on</c> threshold (design 6.3, 7.7). Lives in Core (ruling R15) so the report can record it and the
/// CLI can enforce it without Report referencing Cli.
/// </summary>
public enum FailOn { None, Suspicious, Upgrade, NoRelease }

public static class FailOnWireValues
{
    public static string ToWireValue(this FailOn f) => f switch
    {
        FailOn.None => "none",
        FailOn.Suspicious => "suspicious",
        FailOn.Upgrade => "upgrade",
        FailOn.NoRelease => "no-release",
        _ => throw new ArgumentOutOfRangeException(nameof(f), f, null),
    };
}
