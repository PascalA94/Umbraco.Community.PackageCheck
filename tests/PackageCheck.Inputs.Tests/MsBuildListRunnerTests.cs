using PackageCheck.Core.Model;
using PackageCheck.Inputs;

namespace PackageCheck.Inputs.Tests;

public sealed class MsBuildListRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pc-msbuild-").FullName;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Listed = """
        { "version": 1, "parameters": "--include-transitive",
          "projects": [ { "path": "/x/Site/Site.csproj", "frameworks": [ { "framework": "net10.0",
            "topLevelPackages": [
              { "id": "Umbraco.Cms", "requestedVersion": "17.*", "resolvedVersion": "17.7.2" },
              { "id": "Umbraco.Community.Contentment", "requestedVersion": "[6.2.1, )", "resolvedVersion": "6.2.1" } ],
            "transitivePackages": [ { "id": "Umbraco.Workflow.Core", "resolvedVersion": "17.0.1" } ] } ] } ] }
        """;

    private const string NotRestored = """
        { "version": 1, "parameters": "",
          "problems": [ { "project": "/x/Site/Site.csproj", "level": "error", "text": "No assets file was found. Run restore before running this command." } ],
          "projects": [ { "path": "/x/Site/Site.csproj" } ] }
        """;

    /// <summary>Records every call and answers from a queue, so tests see the exact argument arrays.</summary>
    private const string Host = "/opt/dotnet/dotnet";

    /// <summary>
    /// Records every <c>dotnet list</c> call and answers from a queue; <c>dotnet --version</c> answers <see cref="Sdk"/>
    /// and is counted separately, so tests see the exact list argument arrays.
    /// </summary>
    private sealed class FakeRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);
        public List<(string File, IReadOnlyList<string> Args, string WorkingDirectory, TimeSpan Timeout)> Calls { get; } = [];
        public string Sdk { get; init; } = "10.0.403\n";
        public int VersionCalls { get; private set; }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout, CancellationToken ct)
        {
            if (args is ["--version"]) { VersionCalls++; return Task.FromResult(new ProcessResult(0, Sdk, false)); }
            Calls.Add((fileName, args, workingDirectory, timeout));
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        }
    }

    private string Site(string umbracoVersion = "17.7.2")
    {
        var dir = Path.Combine(_root, "Site");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Site.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Umbraco.Cms" Version="{umbracoVersion}" />
                <PackageReference Include="Umbraco.Community.Contentment" Version="[6.2.1, )" />
              </ItemGroup>
            </Project>
            """);
        return dir;
    }

    private Task<ScanInput> Read(IProcessRunner runner, bool transitive, string umbracoVersion = "17.7.2") =>
        new ProjectReader(Runner(runner)).ReadAsync(Site(umbracoVersion), new InputOptions(transitive, UseMsBuild: true, StopAt: _root), Ct);

    private static MsBuildListRunner Runner(IProcessRunner runner) => new(runner, () => Host);

    [Fact]
    public async Task ParsesTopLevelAndTransitive()
    {
        var runner = new FakeRunner(new ProcessResult(0, Listed, false));

        var refs = await Runner(runner).ListAsync("/x/Site/Site.csproj", includeTransitive: true, Ct);

        Assert.Equal(3, refs.Count);
        var contentment = Assert.Single(refs, r => r.Id == "Umbraco.Community.Contentment");
        Assert.Equal(("6.2.1", true), (contentment.Version!.ToNormalizedString(), contentment.Direct));
        var workflow = Assert.Single(refs, r => r.Id == "Umbraco.Workflow.Core");
        Assert.Equal(("17.0.1", false), (workflow.Version!.ToNormalizedString(), workflow.Direct));
        Assert.Contains("--include-transitive", runner.Calls[0].Args);
        Assert.Equal(TimeSpan.FromMinutes(5), runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task Reader_UsesExactVersions_AndTransitive()
    {
        // A floating Umbraco version and an open range: only MSBuild knows what they resolved to.
        var input = await Read(new FakeRunner(new ProcessResult(0, Listed, false)), transitive: true, umbracoVersion: "17.*");

        Assert.Equal(TransitiveStatus.FromMsBuild, input.Transitive);
        var project = Assert.Single(input.Projects);
        Assert.Equal("17.7.2", project.UmbracoVersion!.ToNormalizedString());
        Assert.Null(Assert.Single(project.Packages, p => p.Id == "Umbraco.Cms").UnresolvedReason);
        Assert.Equal("6.2.1", Assert.Single(project.Packages, p => p.Id == "Umbraco.Community.Contentment").Version!.ToNormalizedString());
        Assert.Contains(project.Packages, p => p is { Id: "Umbraco.Workflow.Core", Direct: false });
    }

    [Fact]
    public async Task CommandFails_FallsBackWithWarning()
    {
        // The open range on Contentment stays unresolved after the fallback, exactly as without --msbuild.
        var input = await Read(new FakeRunner(new ProcessResult(1, NotRestored, false)), transitive: true);

        Assert.Contains(input.Warnings, w => w.Contains("run dotnet restore first"));
        Assert.Equal(TransitiveStatus.Unavailable, input.Transitive);
        var project = Assert.Single(input.Projects);
        Assert.NotNull(Assert.Single(project.Packages, p => p.Id == "Umbraco.Community.Contentment").UnresolvedReason);
        Assert.DoesNotContain(project.Packages, p => !p.Direct);
    }

    [Fact]
    public async Task PathWithSpaces_PassedAsSingleArgument()
    {
        var runner = new FakeRunner(new ProcessResult(0, Listed, false));

        await Runner(runner).ListAsync("/x/My Site/Site.sln", includeTransitive: false, Ct);

        var (file, args, _, _) = runner.Calls[0];
        Assert.Equal(Host, file);
        Assert.Equal(["list", "/x/My Site/Site.sln", "package", "--format", "json", "--no-restore"], args);
    }

    [Fact]
    public async Task Timeout_FallsBackWithWarning()
    {
        var runner = new FakeRunner(new ProcessResult(-1, "", TimedOut: true));

        var input = await Read(runner, transitive: true);

        Assert.Equal(TimeSpan.FromMinutes(5), runner.Calls[0].Timeout);
        Assert.Contains(input.Warnings, w => w.Contains("timed out") && w.Contains("run dotnet restore first"));
        Assert.Equal(TransitiveStatus.Unavailable, input.Transitive);
    }

    [Fact]
    public async Task Sdk8_OmitsNoRestore()
    {
        // SDK 8 rejects --no-restore and never restores on its own (security review M3: decided by version, no retry).
        var runner = new FakeRunner(new ProcessResult(0, Listed, false)) { Sdk = "8.0.427\n" };

        var refs = await Runner(runner).ListAsync("/x/Site/Site.csproj", includeTransitive: false, Ct);

        Assert.DoesNotContain("--no-restore", Assert.Single(runner.Calls).Args);
        Assert.Contains(refs, r => r.Id == "Umbraco.Cms");
    }

    [Theory]
    [InlineData("10.0.403")]
    [InlineData("not a version")]
    public async Task NewerOrUnknownSdk_AlwaysPassesNoRestore_AndNeverRetries(string sdk)
    {
        // A scanned path can put "'--no-restore'" into the error text; that must never lead to a restoring call.
        var runner = new FakeRunner(new ProcessResult(1, "", false, "error: /x/'--no-restore'/Site.csproj has no assets file")) { Sdk = sdk };

        await Assert.ThrowsAsync<MsBuildListException>(() => Runner(runner).ListAsync("/x/'--no-restore'/Site.csproj", false, Ct));

        Assert.Contains("--no-restore", Assert.Single(runner.Calls).Args);
    }

    [Fact]
    public async Task DotnetNotFound_FallsBackWithWarning()
    {
        var runner = new FakeRunner(new ProcessResult(0, Listed, false));

        var input = await new ProjectReader(new MsBuildListRunner(runner, () => null))
            .ReadAsync(Site(), new InputOptions(false, UseMsBuild: true, StopAt: _root), Ct);

        Assert.Empty(runner.Calls);
        Assert.Contains(input.Warnings, w => w.StartsWith("--msbuild:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessThatCannotStart_IsAnMsBuildFailure()
    {
        // Security review L2: a start failure must fall back like any other --msbuild failure, not abort the run.
        await Assert.ThrowsAsync<MsBuildListException>(() => new DotnetProcessRunner()
            .RunAsync(Path.Combine(_root, "no-such-dotnet"), ["--version"], _root, TimeSpan.FromSeconds(10), Ct));
    }

    [Fact]
    public void DotnetHost_IgnoresCurrentDirectoryAndRelativePathEntries()
    {
        // Security review M2: a `dotnet` planted in the scanned folder (the current directory) must never be chosen.
        var cwdLike = Directory.CreateDirectory(Path.Combine(_root, "cwd")).FullName;
        var real = Directory.CreateDirectory(Path.Combine(_root, "sdk")).FullName;
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        File.WriteAllText(Path.Combine(cwdLike, name), "");
        File.WriteAllText(Path.Combine(real, name), "");
        var path = string.Join(Path.PathSeparator, ".", "", "cwd", real);

        var found = DotnetHost.Find(v => v == "PATH" ? path : null);

        Assert.Equal(Path.Combine(real, name), found);
    }

    [Fact]
    public void DotnetHost_PrefersRootedDotnetHostPath()
    {
        var file = Path.Combine(_root, "host-dotnet");
        File.WriteAllText(file, "");

        Assert.Equal(file, DotnetHost.Find(v => v == "DOTNET_HOST_PATH" ? file : null));
        Assert.Null(DotnetHost.Find(v => v == "DOTNET_HOST_PATH" ? "relative/dotnet" : null));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": 2, "projects": [] }""")]
    [InlineData("""{ "version": 1, "projects": [ { "path": "/x", "frameworks": [ { "framework": "net10.0", "topLevelPackages": [ { "id": "A", "resolvedVersion": "not-a-version" } ] } ] } ] }""")]
    [InlineData("""{ "version": 1, "projects": [ { "path": "/x", "frameworks": [ { "framework": "net10.0", "topLevelPackages": [ { "id": "A", "requestedVersion": "1.0.0" } ] } ] } ] }""")]
    public async Task UnexpectedOutput_FallsBackWithWarning(string stdout)
    {
        var input = await Read(new FakeRunner(new ProcessResult(0, stdout, false)), transitive: false);

        Assert.Contains(input.Warnings, w => w.Contains("run dotnet restore first"));
        Assert.Equal(TransitiveStatus.NotRequested, input.Transitive);
    }

    [Fact]
    public async Task WithoutMsBuild_RunnerIsNeverCalled()
    {
        var runner = new FakeRunner(new ProcessResult(0, Listed, false));

        await new ProjectReader(Runner(runner)).ReadAsync(Site(), new InputOptions(false, UseMsBuild: false, StopAt: _root), Ct);

        Assert.Empty(runner.Calls);
    }
}
