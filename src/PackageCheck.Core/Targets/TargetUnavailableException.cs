using PackageCheck.Core.Model;

namespace PackageCheck.Core.Targets;

/// <summary>
/// The target could not be resolved because the feed did not answer (offline, or a source error), not because the input
/// was wrong. Not a <see cref="UsageException"/>: the scan still reports the site, with its packages unknown for this
/// reason, so CI fails closed with exit 3 instead of exiting 2 (plan Task 28b, review P12; preflight 1.34).
/// </summary>
public sealed class TargetUnavailableException(UnknownReason reason, string message) : Exception(message)
{
    public UnknownReason Reason { get; } = reason;
}
