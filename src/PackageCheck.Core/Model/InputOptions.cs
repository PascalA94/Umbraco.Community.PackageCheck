namespace PackageCheck.Core.Model;

/// <param name="Transitive">Also report packages that arrive transitively (from a lock file).</param>
/// <param name="UseMsBuild">Allow asking MSBuild for the package graph.</param>
/// <param name="StopAt">
/// Topmost folder for Directory.Build.props and Directory.Packages.props lookup. Null means the repo root (first
/// .git folder). Tests set it so a fixture never picks up files from the folders above it.
/// </param>
public sealed record InputOptions(bool Transitive, bool UseMsBuild, string? StopAt = null);
