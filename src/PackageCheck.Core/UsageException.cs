namespace PackageCheck.Core;

/// <summary>An input mistake the user can fix; the CLI maps every one to exit code 2.</summary>
public class UsageException(string message) : Exception(message);

/// <param name="readFrom">The solution or project a folder resolved to, named so a wrong pick (a pack solution at the
/// repo root) is obvious; null when the path itself was that file.</param>
public sealed class NoUmbracoProjectException(string path, string? readFrom = null)
    : UsageException($"no Umbraco project found in {path}"
                     + (readFrom is null ? "" : $" (read {System.IO.Path.GetFileName(readFrom)}; pass the site's solution or project file instead)"));

public sealed class PathNotFoundException(string path)
    : UsageException($"path not found: {path}");
