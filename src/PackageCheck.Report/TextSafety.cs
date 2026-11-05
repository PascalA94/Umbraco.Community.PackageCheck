using System.Globalization;
using System.Text;
using Spectre.Console;

namespace PackageCheck.Report;

/// <summary>
/// Encoding for package-controlled text in each output format (design section 4, pitfall S3 [Critical]). Ids, versions,
/// summaries, override and Marketplace text all come from strangers, so every writer passes them through here.
/// </summary>
public static class TextSafety
{
    private const string MarkdownMeta = "\\`*_{}[]()#+-.!|~";

    // GitHub turns @name into a mention when a report is pasted into a PR comment (review M4 F10, M4-S4).
    private const string EntityOnly = "@";

    /// <summary>
    /// Markdown body text: control and format characters dropped, line breaks to spaces, markdown metacharacters
    /// backslash-escaped, then <c>&amp; &lt; &gt; " '</c> HTML-encoded. One pass, so an entity's own <c>#</c> is never
    /// escaped (ruling R17, preflight D5: <c>'</c> renders as <c>&amp;#39;</c>, not <c>&amp;\#39;</c>).
    /// </summary>
    public static string Markdown(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in Flatten(s))
        {
            if (EntityOnly.Contains(c, StringComparison.Ordinal)) sb.Append(CultureInfo.InvariantCulture, $"&#{(int)c};");
            else if (MarkdownMeta.Contains(c, StringComparison.Ordinal)) sb.Append('\\').Append(c);
            else AppendHtml(sb, c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Text inside raw HTML blocks such as <c>&lt;summary&gt;</c>, where backslash escapes would show verbatim
    /// (preflight D5). Markdown metacharacters become numeric entities instead, which render literally whether or not
    /// the renderer parses markdown inside the block.
    /// </summary>
    public static string Html(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in Flatten(s))
        {
            if (EntityOnly.Contains(c, StringComparison.Ordinal)
                || (c is not ('.' or '-' or '(' or ')') && MarkdownMeta.Contains(c, StringComparison.Ordinal)))
                sb.Append(CultureInfo.InvariantCulture, $"&#{(int)c};");
            else AppendHtml(sb, c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Console text: C0/C1 controls (ESC included) and format characters dropped, line breaks to spaces, then Spectre
    /// markup escaped, so <c>\e[31m</c> or <c>[red]</c> prints as inert text on one line (Review Focus 1).
    /// </summary>
    public static string Console(string s) => Markup.Escape(Flatten(s));

    /// <summary>
    /// Plain text for stderr and logs: controls, format characters and line breaks removed as for the console, and any
    /// URL userinfo stripped. No markup escaping, because nothing renders it.
    /// </summary>
    public static string Plain(string s) => Redaction.Text(Flatten(s));

    /// <summary>A markdown link only for an absolute https URL; anything else (javascript:, http:, relative) is text only.</summary>
    public static string Link(string text, string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Markdown(text);
        var safe = uri.AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);
        return $"[{Markdown(text)}]({safe})";
    }

    private static string Flatten(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWasBreak = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            // A valid surrogate pair (emoji, rare CJK) is kept; a lone surrogate is dropped (review M4 F10).
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(c).Append(s[i + 1]);
                i++;
                lastWasBreak = false;
                continue;
            }
            if (c is '\r' or '\n' or '\t' or '\u2028' or '\u2029')
            {
                if (!lastWasBreak) sb.Append(' ');
                lastWasBreak = c is '\r';
                continue;
            }
            lastWasBreak = false;
            var cat = char.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static void AppendHtml(StringBuilder sb, char c) => sb.Append(c switch
    {
        '&' => "&amp;",
        '<' => "&lt;",
        '>' => "&gt;",
        '"' => "&quot;",
        '\'' => "&#39;",
        _ => c.ToString(),
    });
}
