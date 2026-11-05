using PackageCheck.Tests.Support;

namespace PackageCheck.Cli.Tests;

/// <summary>
/// Throwaway site folders. Each has a NuGet.config with only nuget.org and an empty global packages folder, and a
/// <c>.git</c> folder so props lookup stops at the site (a fixture never reads this repo's own props files).
/// </summary>
internal static class TestSites
{
    public static string Fixtures => FixtureHttpHandler.ModeFromEnvironment() == RecordMode.Record
        ? Path.Combine(RepoRoot(), "tests", "Support", "Http", "cli")
        : Path.Combine(AppContext.BaseDirectory, "Http", "cli");

    public static FixtureHttpHandler Handler() => new(Fixtures, FixtureHttpHandler.ModeFromEnvironment());

    public static string Create(string umbracoVersion, params (string Id, string Version)[] packages) =>
        Create(umbracoVersion, SharedGlobalPackages(), packages);

    public static string Create(string umbracoVersion, string globalPackages, params (string Id, string Version)[] packages)
    {
        var root = NewRoot(globalPackages);
        var tfm = NuGet.Versioning.NuGetVersion.Parse(umbracoVersion).Major switch { >= 17 => "net10.0", >= 14 => "net9.0", _ => "net8.0" };
        Directory.CreateDirectory(Path.Combine(root, "Web"));
        var refs = string.Concat(new[] { ("Umbraco.Cms", umbracoVersion) }.Concat(packages)
            .Select(p => $"""    <PackageReference Include="{p.Item1}" Version="{p.Item2}" />{"\n"}"""));
        File.WriteAllText(Path.Combine(root, "Web", "Web.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>{tfm}</TargetFramework></PropertyGroup>
              <ItemGroup>
            {refs}  </ItemGroup>
            </Project>
            """);
        return root;
    }

    /// <summary>A copy of a repo-shape fixture (tests/Support/RepoFixtures) in a throwaway folder.</summary>
    public static string FromFixture(string name)
    {
        var root = NewRoot(SharedGlobalPackages());
        var source = Path.Combine(AppContext.BaseDirectory, "RepoFixtures", name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return root;
    }

    public static string SharedGlobalPackages() => Directory.CreateTempSubdirectory("pc-cli-gp-").FullName;

    private static string NewRoot(string globalPackages)
    {
        var root = Directory.CreateTempSubdirectory("pc-cli-site-").FullName;
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config><add key="globalPackagesFolder" value="{globalPackages}" /></config>
              <packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
              <packageSourceMapping><clear /></packageSourceMapping>
            </configuration>
            """);
        return root;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UmbracoPackageCheck.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
