namespace PackageCheck.Core.Model;

/// <summary>
/// Why a package's verdict is <c>unknown</c>. Wire values are fixed by the report schema (global constraints):
/// offline, unresolved-property, private-feed, not-found, unsafe-archive, conflicting-evidence, source-error.
/// Only <see cref="SourceError"/> counts as "a source failed" for exit 3 (design 7.3, ruling R4).
/// </summary>
public enum UnknownReason { Offline, UnresolvedProperty, PrivateFeed, NotFound, UnsafeArchive, ConflictingEvidence, SourceError }

public static class UnknownReasonExtensions
{
    public static string ToWireValue(this UnknownReason reason) => reason switch
    {
        UnknownReason.Offline => "offline",
        UnknownReason.UnresolvedProperty => "unresolved-property",
        UnknownReason.PrivateFeed => "private-feed",
        UnknownReason.NotFound => "not-found",
        UnknownReason.UnsafeArchive => "unsafe-archive",
        UnknownReason.ConflictingEvidence => "conflicting-evidence",
        UnknownReason.SourceError => "source-error",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
