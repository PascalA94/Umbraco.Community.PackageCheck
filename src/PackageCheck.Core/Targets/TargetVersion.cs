using NuGet.Frameworks;
using NuGet.Versioning;

namespace PackageCheck.Core.Targets;

/// <summary>The Umbraco version a scan judges packages against (design 6.1).</summary>
/// <param name="Requested">What the user typed (<c>17</c>, <c>17.7.2</c>, <c>current</c>, ...), for the report.</param>
public sealed record TargetVersion(string Requested, NuGetVersion Resolved, NuGetFramework Tfm, bool Prerelease)
{
    /// <summary>
    /// The version every range check uses (H01, H02, H03, H19, the upgrade walk). A prerelease target counts as its
    /// release base, so <c>18.0.0-rc2</c> is allowed by <c>[18.0.0, )</c> and excluded by <c>[17.0.0, 18.0.0)</c>;
    /// plain NuGet ordering would give the opposite and false verdicts before a major ships (Pascal, 2026-10-30).
    /// </summary>
    public NuGetVersion RangeVersion => Prerelease ? new NuGetVersion(Resolved.Major, Resolved.Minor, Resolved.Patch) : Resolved;
}
