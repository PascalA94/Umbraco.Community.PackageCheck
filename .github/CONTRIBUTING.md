# Contributing

Thanks for helping. Issues and PRs are welcome; for anything larger than a fix, open an issue first so we can agree the approach.

To add or correct what the tool says about one specific package, use the [umbraco-package-compat](https://github.com/PascalA94/umbraco-package-compat) repository instead. It holds the curated overrides.

## Branches

- `main` is the release branch. Work on a branch from `main` and open a PR; PRs are squash-merged.
- Releases are tags `vX.Y.Z` on `main`. Only maintainers tag.

## Build and test

You need the .NET 10 SDK to build (see `global.json`). The tool itself targets `net8.0` and runs on 8, 9 and 10.

```sh
dotnet build
dotnet test --solution UmbracoPackageCheck.slnx
```

- Tests use xunit.v3 on Microsoft.Testing.Platform. Run one class with `dotnet test --project tests/PackageCheck.Core.Tests --filter-class '*TruthTableTests'`.
- No test touches the network. NuGet and Marketplace responses are replayed from `tests/Support/Http`. The golden tests (`--filter-trait "Category=Golden"`) check real packages against those recordings.
- To refresh the recordings, run the `rerecord` workflow, or locally `PACKAGECHECK_RECORD=1 dotnet test -c Release`, and review the diff.
- Every change to a verdict needs a test that failed first. A change to the truth table also updates `HEURISTICS.md`.

## Try a local build

NuGet caches the first build of every version, so give each local pack a unique version:

```sh
v=0.0.0-local.$(date +%Y%m%d%H%M%S)
dotnet pack src/PackageCheck.Cli -c Release -p:Version=$v -o artifacts
dotnet tool install --tool-path /tmp/pc --add-source artifacts Umbraco.Community.PackageCheck --version $v
/tmp/pc/umbraco-package-check scan path/to/a/site --target 17
```

Use a site you are allowed to scan, or a throwaway one: a `.csproj` with a few public `PackageReference`s is enough. The tool never changes the site.

## Rules for code

- Package contents are hostile input: never load an assembly from a package, never extract to disk, and keep the size caps.
- Credentials and feed tokens must never reach any output, log or cache file.
- Text from packages and feeds is escaped in every report format.
- No static mutable state; services are composed in `ServiceRegistration`.
- Comments explain why, not what.
