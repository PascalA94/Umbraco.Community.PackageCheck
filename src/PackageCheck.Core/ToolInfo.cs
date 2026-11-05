using System.Reflection;

namespace PackageCheck.Core;

/// <summary>Identity of the tool. Lives in Core (ruling R15) so Report, Sources and Cli share one copy.</summary>
public static class ToolInfo
{
    public const string Name = "umbraco-package-check";
    public const string RepositoryUrl = "https://github.com/PascalA94/Umbraco.Community.PackageCheck";

    // SourceLink appends "+<commit sha>" to the informational version; users and the
    // User-Agent header want the plain SemVer.
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// The one User-Agent every outbound HTTP call sends (global constraints, design 7.3). Built here so the NuGet,
    /// Marketplace and overrides clients cannot drift apart (preflight 1.19).
    /// </summary>
    public static string UserAgent { get; } = $"{Name}/{Version} (+{RepositoryUrl})";

    private static string ReadVersion()
    {
        var raw = typeof(ToolInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = raw.IndexOf('+');
        return plus < 0 ? raw : raw[..plus];
    }
}
