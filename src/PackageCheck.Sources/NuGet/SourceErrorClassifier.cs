using System.Net;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;

namespace PackageCheck.Sources.NuGet;

/// <summary>
/// Maps a feed failure to an unknown reason (design 7.3, "What counts as a source failed"). 401/403 anywhere in the
/// chain is <see cref="UnknownReason.PrivateFeed"/> (S1b: no credential provider, so a denied feed is never retried
/// interactively); 404 is <see cref="UnknownReason.NotFound"/>; everything else (DNS, refused, timeout, 5xx or 429
/// after retries, malformed responses) is <see cref="UnknownReason.SourceError"/>, which is what drives exit 3.
/// </summary>
public static class SourceErrorClassifier
{
    public static UnknownReason Classify(Exception e)
    {
        var codes = new List<HttpStatusCode>();
        foreach (var ex in Walk(e))
        {
            if (ex is PackageSourceException pse) return pse.Reason;
            if (ex is HttpRequestException { StatusCode: { } code }) codes.Add(code);
        }

        if (codes.Any(c => c is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) return UnknownReason.PrivateFeed;
        if (codes.Count > 0 && codes.All(c => c == HttpStatusCode.NotFound)) return UnknownReason.NotFound;
        return UnknownReason.SourceError;
    }

    private static IEnumerable<Exception> Walk(Exception e)
    {
        var stack = new Stack<Exception>([e]);
        while (stack.Count > 0)
        {
            var ex = stack.Pop();
            yield return ex;
            if (ex is AggregateException agg)
                foreach (var inner in agg.InnerExceptions) stack.Push(inner);
            else if (ex.InnerException is { } inner)
                stack.Push(inner);
        }
    }
}
