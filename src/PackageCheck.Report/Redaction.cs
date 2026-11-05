using System.Text.RegularExpressions;

namespace PackageCheck.Report;

/// <summary>
/// Last line of defence for credentials in report text (design section 4: feed URLs are written with userinfo stripped).
/// Sources already reduce feed URLs to scheme and host (M3 security fix); this removes any <c>user:token@</c> that
/// reaches a report through another path, such as an exception message or a site warning.
/// </summary>
internal static partial class Redaction
{
    [GeneratedRegex(@"(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*://)[^\s@?#]*@", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UserInfo();

    // Userinfo may contain '/' (base64 tokens; security review M4-S3), so everything up to the '@' that ends the
    // userinfo (stopping at whitespace, '?' or '#') is cut. That over-redacts a URL with '@' in its path, which is the safe direction.
    public static string Text(string text) => UserInfo().Replace(text, "${scheme}");

    public static string? NullableText(string? text) => text is null ? null : Text(text);
}
