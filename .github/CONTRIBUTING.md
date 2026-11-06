# Contributing

Thanks for helping. Issues and PRs are welcome; for anything larger than a fix, open an issue first so we can agree the approach.

To add or correct what the tool says about one specific package, add an override: see [Overrides](#overrides) below.

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

## Overrides

`overrides/` holds the curated community overrides (CC0, see `overrides/LICENSE`). Every entry changes what the tool tells people about a package, so the rules are strict.

### Governance

- Changes by PR only. Every entry needs a public evidence link.
- For `broken`, raise an issue on the package's repo first.
- Authors can claim their file. Their `works` wins unless there's an open `broken` report with a repro.
- Entries expire on `reviewBy` or at the package's next major.
- Merging needs two maintainers: Pascal plus one community co-maintainer. Until a co-maintainer joins, there is only one, and PRs are merged after one approving review. If you'd like to help maintain the overrides, open an issue.

### Adding or changing an entry

1. One file per package: `overrides/<package-id-in-lower-case>.yml`.
2. Fill in every field (see `schema/override.schema.json` and the example below). `evidence` must be a public `https` link with no user info: the issue you raised on the package's repo, a forum post or a reproduction. Private links, client names and screenshots of client sites are not accepted.
3. Check it and refresh the snapshot bundled in the tool (needs the .NET 10 SDK), and commit both:

   ```sh
   dotnet run tools/compile.cs -- --check --seed data/overrides.seed.json
   ```

4. Open a PR. The `Overrides` workflow runs the same check and fails if the snapshot is out of date. After the merge it republishes the `overrides` release.

```yaml
id: Astroboard
entries:
  - packageVersions: "[1.0.0, )"     # NuGet range of the package versions this is about
    umbraco: "[14.0.0, )"            # NuGet range of the Umbraco versions this is about
    status: broken                   # works | broken | partial
    summary: "Legacy AngularJS package.manifest only; does not load in the new backoffice"
    evidence: https://github.com/<owner>/<repo>/issues/<n>
    reportedBy: <github-handle>
    verifiedOn: "2026-10-29"
    reviewBy: "2027-10-29"           # optional; defaults to 12 months after verifiedOn
    replacement: null                # optional package id
```

Quote the dates so YAML keeps them as text. The Umbraco majors table (`data/umbraco-versions.json`) is published with the overrides and follows the same PR rules.
