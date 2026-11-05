using System.Globalization;
using System.Text;

namespace PackageCheck.Core;

/// <summary>
/// Package-controlled text (zip entry names, BasePath, folder names) made inert at the source (security review M3, S7):
/// control and format characters (ESC/OSC sequences, bidi overrides) become literal <c>\uXXXX</c> and the value is capped.
/// Each report writer still encodes for its own format (global constraints, pitfall S3); this is the first line.
/// </summary>
public static class PackageText
{
    public const int MaxLength = 200;

    public static string Safe(string text)
    {
        var sb = new StringBuilder(Math.Min(text.Length, MaxLength + 8));
        foreach (var c in text)
        {
            if (sb.Length >= MaxLength - 1) { sb.Append('…'); break; }
            var cat = char.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
                sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
