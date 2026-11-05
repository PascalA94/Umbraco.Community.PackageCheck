using NuGet.Frameworks;

namespace PackageCheck.Core.Facts;

/// <summary>
/// Folder names under <c>lib/</c> are package-controlled. <see cref="NuGetFramework.ParseFolder(string)"/> throws on some
/// (for example "portable-net45+win-8"), and one such folder must not abort a scan (security review M3, S3).
/// </summary>
public static class Frameworks
{
    public static NuGetFramework SafeParseFolder(string folder)
    {
        try { return NuGetFramework.ParseFolder(folder); }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException) { return NuGetFramework.UnsupportedFramework; }
    }

    public static bool IsRecognised(string folder) => !SafeParseFolder(folder).IsUnsupported;
}
