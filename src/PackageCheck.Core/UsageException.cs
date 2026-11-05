namespace PackageCheck.Core;

/// <summary>An input mistake the user can fix; the CLI maps every one to exit code 2.</summary>
public class UsageException(string message) : Exception(message);

public sealed class NoUmbracoProjectException(string path)
    : UsageException($"no Umbraco project found in {path}");

public sealed class PathNotFoundException(string path)
    : UsageException($"path not found: {path}");
