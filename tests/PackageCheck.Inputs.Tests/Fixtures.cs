namespace PackageCheck.Inputs.Tests;

internal static class Fixtures
{
    public static string Path(string relative) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "RepoFixtures", relative);
}

/// <summary>A throwaway folder tree, so skip-list and depth rules need no committed bin/obj/node_modules folders.</summary>
internal sealed class TempTree : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("pc-inputs-").FullName;

    public string Add(string relative)
    {
        var full = System.IO.Path.Combine(Root, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
        return full;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
