using System.Globalization;
using PackageCheck.Core;

namespace PackageCheck.Cli.Commands;

/// <summary><c>--max-age</c>: a positive whole number of minutes, hours or days (<c>30m</c>, <c>6h</c>, <c>1d</c>).</summary>
public static class DurationParser
{
    public static TimeSpan Parse(string text)
    {
        var t = text.Trim();
        if (t.Length >= 2
            && int.TryParse(t[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n > 0)
        {
            switch (t[^1])
            {
                case 'm': return TimeSpan.FromMinutes(n);
                case 'h': return TimeSpan.FromHours(n);
                case 'd': return TimeSpan.FromDays(n);
            }
        }
        throw new UsageException($"--max-age '{text}' is not a duration such as 30m, 6h or 1d");
    }
}
