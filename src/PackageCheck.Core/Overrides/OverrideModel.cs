using NuGet.Versioning;
using PackageCheck.Core.Model;

namespace PackageCheck.Core.Overrides;

/// <summary>Override status as the data repo spells it (design 7.8). <c>broken</c> is a wire value only; prose says "reported not working" (ruling R5).</summary>
public enum OverrideStatus { Works, Broken, Partial }

public enum OverrideOrigin { Community, Local }

/// <summary>One override entry (design 7.8). <paramref name="Umbraco"/> is matched against the target's release base.</summary>
public sealed record OverrideEntry(
    string Id,
    VersionRange PackageVersions,
    VersionRange Umbraco,
    OverrideStatus Status,
    string Summary,
    Uri Evidence,
    string? ReportedBy,
    DateOnly VerifiedOn,
    DateOnly ReviewBy,
    string? Replacement,
    OverrideOrigin Origin);

public sealed record OverrideSet(IReadOnlyList<OverrideEntry> Entries, string? SourceLabel)
{
    /// <summary>No overrides: what golden and engine tests use, so they prove the computed verdict (global constraints).</summary>
    public static OverrideSet Empty { get; } = new([], null);
}

/// <summary>An override that replaced a verdict. The computed verdict is always kept (design 7.6, Pascal 2026-10-30).</summary>
public sealed record OverrideApplied(OverrideEntry Entry, Verdict ComputedVerdict, bool Stale, string Label);
