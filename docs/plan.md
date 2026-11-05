# Umbraco.Community.PackageCheck 0.1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Umbraco.Community.PackageCheck` 0.1, a dotnet tool (`umbraco-package-check`) that tells a developer, per Umbraco package, whether it really works on a target Umbraco version and what to move it to, by reading NuGet metadata and looking inside the nupkg.

**Architecture:** Six `net8.0` projects. `Inputs` turns `.sln`/`.slnx`/`.csproj`/folders into package references; `Sources` fetches facts (NuGet via `NuGet.Protocol`, Marketplace, overrides) behind a cache; `Inspect` reads nupkgs as untrusted zip streams; `Core` turns facts into evidence (one class per heuristic) and evidence into a verdict through the design's truth table; `Report` renders JSON, markdown and console; `Cli` wires it with System.CommandLine and maps results to exit codes. All I/O happens in a facts-collection step, so every heuristic and the truth table are pure functions that test without fakes.

**Tech Stack:** .NET SDK 10 building `net8.0` with `RollForward=Major`; NuGet.Protocol, NuGet.Packaging, NuGet.Versioning, NuGet.Configuration; System.Reflection.Metadata; System.CommandLine 2.x; Spectre.Console; YamlDotNet (local overrides); xUnit v3, Verify (snapshots), JsonSchema.Net (contract tests); GitHub Actions. Take the latest stable version of each at Task 1 and pin it in `Directory.Packages.props`.

**Spec:** `<workspace>/09-package-check/design.md` (copied to `docs/design.md` in the repo). Its "Resolved by Pascal (2026-10-30)" section is final. Background: `scope.md` beside it, and the portfolio `README.md` one folder up.

**Repo:** `PascalA94/Umbraco.Community.PackageCheck` (private, empty), cloned to `~/Projects/Umbraco.Community.PackageCheck`. All paths below are relative to that folder unless absolute.

**Calendar (today is Fri 30 Oct 2026; deadline 14 Dec 2026):**

| Milestone | Tasks (in run order) | Aim to finish | Done when (design section 8) |
|---|---|---|---|
| M0 spikes | 1, 2, 2b, 3-7 | Thu 5 Nov; **results review with Pascal Fri 6 Nov** | 4 golden verdicts print (computed, no overrides) |
| M1 inputs | 8-11 | Fri 13 Nov | 10 repo-shape fixtures pass |
| M2 metadata verdicts | 12-18 | Sun 22 Nov | `ok`, `upgrade`, `no-release`, `replaced` criteria pass |
| M3 inspection, cache, offline | 19-24 | Sun 29 Nov | `suspicious` criteria pass; offline makes no HTTP |
| M4 reports, CLI, packaging, rehearsal | 25-27, 28a, 35a, 28b, 28c, 29, 30, 30b | Fri 4 Dec | `dotnet tool install` works; schema published; `0.1.0-rc.1` published by `release.yml` |
| M5 overrides repo, README, release | 31-34, 35, 36 | **Wed 9 Dec** | 0.1.0 on NuGet and listed on the Marketplace |

Wed 9 Dec leaves three working days of buffer before the 14 Dec deadline. Task numbers are kept from the reviewed plan so `plan-review.md` references still resolve; new tasks carry a letter suffix (2b, 28a-c, 30b, 35a). There are 41 tasks.

**Cut rule:** check on **Mon 30 Nov** (is M3 done?) and again on **Fri 4 Dec** (is `0.1.0-rc.1` out?). If either is missed, cut in this order until the dates hold, and record each cut in `docs/decisions.md`:
1. `--msbuild` (Task 34)
2. H11 (Task 21, marked cuttable)
3. H17 (Task 22)
4. multi-site matrix rendering (Task 26, marked cuttable)
5. `rerecord.yml` (Task 30 Step 2); fixtures are then re-recorded by hand before each release
6. `explain` (Task 29, explain half; `cache` stays). This drops a command listed in design 6.1, so tell Pascal before cutting
7. live community override fetch (Task 32); the tool ships the bundled snapshot only and `--overrides` still works. Design 7.8 expects the live fetch in M5, so tell Pascal before cutting

Items 1-4 are the design's order (design section 4); items 5-7 extend it (plan review P16). Never cut offline correctness, exit code 3, the release rehearsal (Task 30b), or anything in the Security constraints below.

## Global Constraints

**Harness rules**
- Claude never runs `git commit`. Every commit step is a **Checkpoint (Pascal commits)** with a `git add` line and a suggested message. No `Co-Authored-By` or any AI attribution in messages, README, release notes or code comments.
- **CI and GitHub rules (Pascal, 2026-11-04):** the portfolio `README.md` section "Build approach → CI and GitHub rules" overrides this plan's CI and GitHub tasks: public repo before the first CI push, fast PR checks only, e2e and SQL Server in a manual `release-checks.yml`, no credentials in workflows, and the first GitHub CI run planned as its own step before any release tag.
- Do not change the git identity in the repo. It is set by Pascal's `includeIf`.
- TDD: write the failing test, watch it fail, implement, watch it pass. Every task ends with a verification command and its expected output.
- The implementer runs their own change before handing off. Tasks 35a and 35 exercise the whole tool against real sites, read-only.
- Comments explain why, not what. Link the design section or decision where one exists.

**Versions and identity (verbatim from the spec)**
- NuGet ID `Umbraco.Community.PackageCheck`; command `umbraco-package-check`. Phase 3 ID `Umbraco.Community.PackageCheck.Backoffice`. Never rename a published ID.
- CLI runtime `net8.0` with `RollForward=Major`, so it runs on the 8, 9 and 10 SDKs.
- No Umbraco major is hard-coded anywhere in `src/`. Majors come from the feed or `umbraco-versions.json`. A test in Task 13 greps `src/` for literal majors in version logic.
- Licence: MIT for code; CC0 for the `umbraco-package-compat` data repo.
- Report JSON `schemaVersion: 1`. Adding optional fields is fine; removing or renaming a field, or changing an enum value, bumps `schemaVersion`.
- Enum wire values are exactly: verdicts `ok|upgrade|no-release|suspicious|replaced|unknown|not-umbraco`; confidence `high|medium|low|null`; reasons `offline|unresolved-property|private-feed|not-found|unsafe-archive|conflicting-evidence|source-error`.
- Package ids compare case-insensitively everywhere (`StringComparer.OrdinalIgnoreCase`); reports show the id as the project wrote it.
- A prerelease target (e.g. `18.0.0-rc2`) is compared against its release base (`18.0.0`) in every range check: heuristics use `TargetVersion.RangeVersion`, never `Resolved`. The report carries `target.prerelease: true` (design 6.1, Pascal 2026-10-30).
- Several paths: a path with no Umbraco project is warned about and skipped; exit 2 only when no path has a site; a missing path is exit 2 (design 6.1, Pascal 2026-10-30).
- Golden and engine tests prove the **computed** verdict with `OverrideSet.Empty`. Only the tests named for overrides load an override set.

**Behaviour rules (verbatim intent from the spec)**
- The word is "suspicious", never "broken", in any output, README or listing copy. Evidence is always shown.
- When offline or a source fails, the verdict is `unknown` with a reason, never `ok`.
- Never exit 0 with `--fail-on` set when a required source failed: that is exit 3. If both 1 and 3 apply, return 3.
- Overrides are always labelled and linked; the computed verdict is kept in `override.computedVerdict`.
- Never call the Marketplace `GET /api/v1.0/validatePackage/{id}` endpoint.
- Marketplace and overrides fetch failures never cause exit 3.
- HTTP: concurrency 8; retry 429 and 5xx with backoff, max 3 tries; 30 s timeout per call; `User-Agent: umbraco-package-check/<version> (+https://github.com/PascalA94/Umbraco.Community.PackageCheck)`.
- The tool reports only. It never edits a csproj, props, lock file or anything in a scanned folder.

**Security (non-negotiable; from design section 4 and `~/.claude/references/umbraco-pitfalls.md`)**
- Every nupkg is hostile. Never `Assembly.Load`, `LoadFrom`, `LoadFile`, `AssemblyLoadContext.LoadFrom*`, `MetadataLoadContext`, or reflection-invoke on package content. Only `System.Reflection.Metadata` over bytes.
- Read nupkgs as zip streams. Never extract to disk. Reject entries whose full name contains `..` segments, is rooted, or starts with `/` or `\` or a drive letter: the package becomes `unknown`, reason `unsafe-archive`.
- Caps: nupkg 250 MB, 20,000 entries, 50 MB per inspected entry. Over the size cap, stop and fall back to metadata-only evidence.
- Entry sizes in the zip central directory are not trusted: every inspected entry is read through a byte-counting stream that enforces the per-entry cap on bytes actually read, and a 500 MB cap applies to the total bytes read from one nupkg (architect's call, plan review P7). Breaching either is `unsafe-archive`.
- Credentials come only from NuGet credential providers and `NuGet.config`. They never appear in console, JSON, markdown, logs, exception messages or cache files. Feed URLs are written with userinfo stripped.
- All package-controlled text (ids, versions, summaries, override text, Marketplace fields) is escaped for markdown and HTML in md output, escaped for Spectre markup and stripped of control characters in console output. README text is never copied in. (Pitfall S3 [Critical], applied to our outputs.)
- No static mutable state and no service locator: services are constructed through `Microsoft.Extensions.DependencyInjection` in `Cli`. (Pitfalls P1, and P2 [Critical]; reflection tests in Task 28c enforce it.)
- 0.1 ships no web endpoint, controller, view, Surface controller or content model, so pitfalls S1, S5, P5 and D1 [Critical] have no surface in 0.1. The Backoffice placeholder package (Task 1) contains no types at all. Phase 3 must apply S1 (`ManagementApiControllerBase`, `[VersionedApiBackOfficeRoute]`, `RequireAdminAccess`), P2, P7 and S3 (no `unsafeHTML`) as written in design section 4.
- No GitHub workflow in this repo uses `pull_request_target`. Every workflow declares least-privilege `permissions:` and pins third-party actions by SHA.
- Publishing to NuGet uses an API key scoped to `Umbraco.Community.PackageCheck*` only, or trusted publishing. Pascal holds it; Claude never handles it.

**Not in 0.1:** code rules (D1, link to Umbraco17Check), H10, SARIF, the GitHub Action, the mf-harness job, the backoffice package beyond its placeholder.

## Review Focus

These are inputs the spec implies but no acceptance criterion names. Each line has its test in the owning task.

1. **Terminal and markup injection in console output.** Any package-controlled text that reaches the console (id, version text, NuGet summary or deprecation text, Marketplace fields, override summary, evidence detail, site warnings naming a project path) containing ANSI escape sequences, `[red]`-style Spectre markup or newlines must print as inert text on one line. Test: Task 27, `Console_PackageText_IsInert` (a theory over every one of those sources).
2. **Floating or ranged versions in CPM or csproj** (`17.*`, `[13.0.0,14.0.0)`, `$(UmbracoVersion)` defined in `Directory.Build.props`). With a lock file the locked version wins; with a resolvable property it is substituted; otherwise the package is `unknown` with reason `unresolved-property` and the detail names the raw text. Test: Task 9, `FloatingVersion_WithoutLock_IsUnresolved` and `OpenRange_WithoutLock_IsUnresolved`, and Task 10, `LockFile_ResolvesFloatingVersion`.
3. **One package across projects in one site**, at different versions or the same version. Each distinct (id, version) is judged once and listed with every project that uses it, so a test project on an old version is not hidden and a shared version is not shown twice. Test: Task 11, `SamePackage_TwoVersions_TwoRows` and `SamePackage_SameVersion_TwoProjects_OneRow`.
4. **Solution entries that point at missing or unreadable project files** (deleted projects, solution folders, a malformed csproj or solution). The project is skipped with a site warning naming it; the run continues; the warning reaches every output format. Test: Task 8, `MissingProjectInSolution_IsSkippedWithWarning`; Task 9, `MalformedCsproj_IsSkippedWithWarning`; Task 28a, `SiteWarnings_AppearInConsoleJsonAndMd`.
5. **Interrupted or parallel runs leaving a corrupt cache entry, a stale `.tmp` file or a temp nupkg.** A cache file that fails to parse is treated as a miss and replaced; `.tmp` files are never read and are removed once older than 1 hour; temp nupkgs are deleted in a `finally`; two runs on one cache dir, in two processes, both succeed. Test: Task 23, `CorruptCacheEntry_IsAMiss`, `ParallelRuns_ShareCacheSafely`, `ParallelRuns_TwoProcesses_ShareCacheSafely` and `StaleTmpFiles_IgnoredAndCleaned`; Task 21, `TempNupkg_IsDeletedOnFailure`.

---

## File structure

```
UmbracoPackageCheck.slnx
Directory.Build.props            net8.0, Nullable, TreatWarningsAsErrors, Deterministic, SourceLink, LangVersion latest
Directory.Packages.props         central package versions
global.json                      SDK 10.0.x, rollForward latestFeature
data/overrides.seed.json         seed overrides (astroboard), embedded as the bundled snapshot
data/umbraco-versions.json       majors 13-18: lts, eol, tfm
schema/report.schema.json        JSON Schema for schemaVersion 1, embedded and published
docs/design.md  docs/plan.md  docs/spikes.md  docs/decisions.md  docs/truth-table-changes.md
spikes/                          M0 throwaway console apps, not in the solution
placeholders/Backoffice/         0.0.1-alpha ID reservation, no types
src/PackageCheck.Core/           UsageException.cs (+ subclasses), Model/, Facts/, Heuristics/ (H01.cs ... one per id), Verdicts/, Targets/, Overrides/
src/PackageCheck.Inputs/         FolderDiscovery, SolutionFileReader, MsBuildProperties, CentralPackageVersions,
                                 ProjectFileReader, LockFileReader, ProjectReader, MsBuildListRunner (cuttable)
src/PackageCheck.Sources/        NuGet/, Marketplace/, Overrides/, Cache/, Http/
src/PackageCheck.Inspect/        ArchiveGuard, CountingReadStream, NupkgInspector, StaticWebAssetsProps, MetadataTypeScanner, BannedSymbols.txt
src/PackageCheck.Report/         Model/, ReportBuilder, JsonReportWriter, MarkdownReportWriter, ConsoleReportWriter, TextSafety
src/PackageCheck.Cli/            Program, CliApp, Commands/, ScanOrchestrator, ExitCodes, ServiceRegistration
tests/PackageCheck.<Project>.Tests/   one test project per src project
tests/PackageCheck.Golden.Tests/      real packages pinned by hash, Category=Golden
tests/Support/                   FixtureHttpHandler, NupkgBuilder, RepoFixtures/, Http/ (recorded responses), StubAssemblies/
.github/workflows/ci.yml  golden.yml  rerecord.yml  release.yml
```

Library projects are referenced by `PackageCheck.Cli` and bundled inside the tool nupkg; only the tool is published (design 7.1).

**Interface order (plan review P9).** Every type is created in the first task that compiles against it, so each task builds on its own:
- Task 8 creates `UsageException`, `NoUmbracoProjectException` and `PathNotFoundException` in Core. There is no `NoProjectFoundException`.
- Task 14 creates the `InspectionResult` and `MarketplaceInfo` records (shape only) because `PackageFacts` holds them; Tasks 16 and 19 fill them.
- Task 17 creates the override model records (`OverrideStatus`, `OverrideOrigin`, `OverrideEntry`, `OverrideApplied`) because `PackageVerdict` holds one; Task 18 adds the logic.
- `FactsCollector` gains one optional constructor parameter per later task (marketplace in 16, inspector in 21, project file system in 22), always appended last with a `null` default, so earlier tests keep compiling.

---

## M0: spikes (go/no-go)

Time-box: 2 days total for Tasks 2 to 5. Tasks 2 to 6 write their findings to `docs/spikes.md` under a heading per spike: date, what was run, result, decision, and plan changes applied. Spike code lives in `spikes/` and is not production code; later tasks may lift pieces of it into `src/` with tests.

### Task 1: Clone, scaffold the solution, and build the two placeholder packages

**Files:**
- Create: `UmbracoPackageCheck.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.gitignore`, `.editorconfig`, `LICENSE` (MIT, Pascal Meyer), `README.md` (placeholder), `docs/design.md`, `docs/plan.md`, `docs/spikes.md`, `docs/decisions.md`
- Create: `src/PackageCheck.{Core,Inputs,Sources,Inspect,Report,Cli}/*.csproj`, `tests/PackageCheck.{Core,Inputs,Sources,Inspect,Report,Cli}.Tests/*.csproj`
- Create: `src/PackageCheck.Cli/Program.cs` (placeholder behaviour), `placeholders/Backoffice/Umbraco.Community.PackageCheck.Backoffice.csproj`, `placeholders/Backoffice/README.md`
- Create: `.github/workflows/ci.yml`
- Test: `tests/PackageCheck.Cli.Tests/PlaceholderTests.cs`

**Interfaces:**
- Produces: the solution every later task builds in; `ToolInfo.Version` read from `AssemblyInformationalVersionAttribute` (no `+sha` suffix in output).

- [ ] **Step 1: Clone and copy the docs**

```bash
git clone git@github.com:PascalA94/Umbraco.Community.PackageCheck.git ~/Projects/Umbraco.Community.PackageCheck
mkdir -p ~/Projects/Umbraco.Community.PackageCheck/docs
cp "<workspace>/09-package-check/design.md" "<workspace>/09-package-check/plan.md" ~/Projects/Umbraco.Community.PackageCheck/docs/
git -C ~/Projects/Umbraco.Community.PackageCheck config user.email
```
Expected: clone succeeds (empty repo warning is fine); last line prints `5427750+PascalA94@users.noreply.github.com`. If it prints anything else, stop and tell Pascal; do not change it.

- [ ] **Step 2: Scaffold projects** with `dotnet new classlib`/`console`/`xunit3` and `dotnet new sln --format slnx`. `Directory.Build.props` sets `TargetFramework net8.0`, `Nullable enable`, `TreatWarningsAsErrors true`, `Deterministic true`, `ContinuousIntegrationBuild` on CI, `Microsoft.SourceLink.GitHub`, `ManagePackageVersionsCentrally true`. The Cli csproj carries the packaging block from design section 10 verbatim, plus `PackageProjectUrl`, `RepositoryUrl`, `PackageReadmeFile`. Do **not** add the `umbraco-marketplace` tag yet (Task 6 decides).

- [ ] **Step 3: Write the failing test**

```csharp
[Fact] public async Task Placeholder_PrintsComingSoon_AndExits2()
{
    var (code, stdout) = await CliHarness.RunAsync("scan", ".");
    Assert.Equal(2, code);
    Assert.Contains("Umbraco Package Check: coming soon, see https://github.com/PascalA94/Umbraco.Community.PackageCheck", stdout);
}
```
`CliHarness.RunAsync(params string[] args) -> Task<(int Code, string Stdout)>` lives in `tests/Support` and calls `CliApp.RunAsync(args, TextWriter out, TextWriter err)`.

- [ ] **Step 4: Run it**

Run: `dotnet test tests/PackageCheck.Cli.Tests --filter Placeholder_PrintsComingSoon_AndExits2`
Expected: FAIL, build error `CliApp` not found.

- [ ] **Step 5: Implement `static Task<int> CliApp.RunAsync(string[] args, TextWriter stdout, TextWriter stderr)`** returning 2 after printing the line. `Program.Main` forwards to it. This file is replaced in Task 28a.

- [ ] **Step 6: Placeholder Backoffice package.** A class library `net8.0` with `PackageId Umbraco.Community.PackageCheck.Backoffice`, version `0.0.1-alpha`, MIT, README "Placeholder reserving this ID for the phase 3 backoffice view. Contains no code." No source files, no Umbraco reference.

- [ ] **Step 7: CI workflow.** `ci.yml`: `pull_request` and `push` to `main`, `permissions: contents: read`, `actions/checkout` and `actions/setup-dotnet` pinned by SHA, SDK 8, 9 and 10 installed, `dotnet build -c Release` then `dotnet test -c Release --filter "Category!=Golden"`.

- [ ] **Step 8: Verify build, test and both packs**

```bash
cd ~/Projects/Umbraco.Community.PackageCheck
dotnet test --filter "Category!=Golden"
dotnet pack src/PackageCheck.Cli -c Release -p:Version=0.0.1-alpha -o artifacts
dotnet pack placeholders/Backoffice -c Release -o artifacts
dotnet tool install --tool-path /tmp/pc-alpha --add-source artifacts Umbraco.Community.PackageCheck --version 0.0.1-alpha
/tmp/pc-alpha/umbraco-package-check scan . ; echo "exit=$?"
```
Expected: tests `Failed: 0`; two nupkgs in `artifacts/`; last lines print the coming-soon message and `exit=2`. Remove `/tmp/pc-alpha` afterwards.

- [ ] **Step 9: Checkpoint (Pascal commits):** `git add .` / suggested message `chore: scaffold solution, placeholder tool and backoffice ID packages`

- [ ] **Step 10: Pascal publishes both `0.0.1-alpha` nupkgs** from `artifacts/` to NuGet with his scoped key, leaves them listed (design section 10), and confirms both IDs show on nuget.org. This is Pascal's action, not Claude's.

### Task 2: Spike S1, NuGet.Protocol range checks and the HTTP seam (go/no-go)

**Files:**
- Create: `spikes/S1-nuget-protocol/` (console app), section `S1` in `docs/spikes.md`

**Interfaces:**
- Produces: the decision Task 12 builds on: whether `IPackageMetadataSource` sits on NuGet.Protocol, and where the HTTP seam for fakes and the offline guard lives.

- [ ] **Step 1: Fetch registration for `Umbraco.Community.Contentment`** with `PackageMetadataResource` from a `SourceRepository` built from `Settings.LoadDefaultSettings(<a temp folder with a NuGet.config>)`. Print each version's dependency groups.
- [ ] **Step 2: Pick the dependency group** for `Umbraco.Cms` 17.7.2's TFM using `NuGetFrameworkUtility.GetNearest`, with the TFM read from `Umbraco.Cms` 17.7.2's own dependency groups. Print the `Umbraco.Cms*` range for 6.2.1 and 6.3.3 and whether each allows 17.7.2.
- [ ] **Step 3: Lowest-major-highest-patch selection** over all stable versions newer than 6.2.1. Print the pick and the highest overall.
- [ ] **Step 4: HTTP seam.** Build the `SourceRepository` with a custom `HttpHandlerResourceV3Provider` that wraps a counting `DelegatingHandler`. Show that every request from Steps 1-3 passes through it, and that a handler returning canned responses from files can replace the network.
- [ ] **Step 5: Source mapping.** With a `NuGet.config` that maps `Umbraco.*` to nuget.org only and adds a dummy second feed, show no request goes to the dummy feed for Contentment.
- [ ] **Step 6: Record results in `docs/spikes.md`.**

**Pass criterion (go):** Step 2 shows 6.2.1 does not clear 17.7.2 and 6.3.3 does; Step 3 prints `6.3.3` (and a 7.x as highest overall); Step 4 counts more than zero requests and serves at least one canned response; Step 5 makes zero requests to the dummy feed.

**If it fails (no-go), change the plan:**
- Steps 1-2 fail (groups missing, ranges wrong): Task 12 switches to a hand-written `HttpClient` reader of the registration and flat-container resources discovered from the service index, keeping `NuGet.Configuration` for feeds and credentials and `NuGet.Versioning` for range maths. Add 2 days; cut `--msbuild` (Task 34) immediately.
- Step 3 fails: no plan change; `UpgradeSelector` (Task 17) is already hand-written.
- Step 4 fails: move the seam up. Task 12 records fixtures as serialised `PackageVersions`/`PackageDetails` DTOs, and the Task 23 offline test asserts the `ISourceRepositoryFactory` is never invoked instead of asserting the handler is never hit. Record this as a deviation from design 7.5 in `docs/decisions.md`.
- Step 5 fails: Task 12 applies `PackageSourceMapping` itself before choosing feeds.

- [ ] **Step 7: Checkpoint (Pascal commits):** `git add spikes/S1-nuget-protocol docs/spikes.md` / suggested message `spike: S1 NuGet.Protocol range checks and HTTP seam`

### Task 2b: Spike S1b, credential providers when headless (go/no-go)

**Files:**
- Create: `spikes/S1b-credential-provider/` (console app), section `S1b` in `docs/spikes.md`

**Interfaces:**
- Produces: the decision Task 12 builds on: whether `FeedSettings` enables NuGet credential providers in non-interactive mode, or reads `NuGet.config` credentials only.

Pascal names one real private feed that needs a credential provider (for example an Azure Artifacts feed used by a Manifesto site) and the package id to query. Never print or copy that site's `NuGet.config` contents or any token into the spike output or `docs/spikes.md`; record the feed host only.

- [ ] **Step 1: Authenticated run.** Call `DefaultCredentialServiceUtility.SetupDefaultCredentialService(logger, nonInteractive: true)`, build a `SourceRepository` from `Settings.LoadDefaultSettings(<folder with that NuGet.config>)`, and fetch versions for the named id, with the provider already signed in. Time it with a 30 s `CancellationToken`.
- [ ] **Step 2: Signed-out run.** Repeat with the provider's token cache cleared (or `NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED=false` and no env credentials), stdin redirected from `/dev/null`, inside `timeout 60`.
- [ ] **Step 3: CI-shaped run.** Repeat Step 2 with `CI=true`, run as a background job with no TTY attached. Record exit, elapsed time, and the exception type and message (redacted).
- [ ] **Step 4: Record results in `docs/spikes.md`.**

**Pass criterion (go):** Step 1 returns versions; Steps 2 and 3 each finish within 30 s with a 401-class failure that `SourceErrorClassifier` can map to `private-feed`, and no prompt is printed.

**If it fails (no-go), change the plan:**
- Any run prompts or exceeds 30 s: Task 12 does not set up the credential service at all. `FeedSettings` uses `NuGet.config` `packageSourceCredentials` only (including `%ENV%` references). README (Task 33) says private feeds need credentials in `NuGet.config` or env vars, and CI users pass them that way. Record in `docs/decisions.md`.
- Step 1 fails but Steps 2-3 are fast: same fallback; note that the provider path is unsupported in 0.1.

- [ ] **Step 5: Checkpoint (Pascal commits):** `git add spikes/S1b-credential-provider docs/spikes.md` / suggested message `spike: S1b headless credential providers`

### Task 3: Spike S2, inspect nupkgs from the zip stream (go/no-go)

**Files:**
- Create: `spikes/S2-nupkg-inspect/`, section `S2` in `docs/spikes.md`, `tests/PackageCheck.Golden.Tests/golden-packages.json`

**Interfaces:**
- Produces: the exact entry-path patterns for H07 and H08, the dependency depth H07 needs, and `golden-packages.json` entries `{ "id", "version", "sha512" }` used by Task 24.

- [ ] **Step 1: Download** `astroboard 1.1.3`, `Umbraco.Community.SimpleDashboards 17.2.0`, `Umbraco.Workflow 17.5.4`, its `Umbraco.Workflow.StaticAssets` dependency at the resolved version, `Crumpled.RobotsTxt 3.1.1`, `MediaWiz.Forums 14.0.0`, Contentment `4.7.0`, `6.2.1`, `6.3.3`, `7.2.1`, the latest `Our.Umbraco.Community.Contentment`, and Skybrud Redirects `17.1.0-beta` into a scratch folder outside the repo.
- [ ] **Step 2: Open each with `ZipArchive` in read mode** (no extraction) and list entries matching `**/umbraco-package.json`, `**/package.manifest`, `build*/Microsoft.AspNetCore.StaticWebAssets.props`, `lib/*/` folders, and `*.js`/`*.mjs` under `staticwebassets/` or `App_Plugins/`.
- [ ] **Step 3: Read `BasePath`** from the props XML for Workflow.StaticAssets and Crumpled.RobotsTxt.
- [ ] **Step 4: Record** the paths, the lib TFMs, and each nupkg's sha512 (base64, NuGet's format) into `golden-packages.json` and `docs/spikes.md`.

**Pass criterion (go):** astroboard shows a `package.manifest` and no `umbraco-package.json`; Workflow 17.5.4 has no `umbraco-package.json` of its own but its StaticAssets dependency has one under `staticwebassets/` with a readable `BasePath`; Crumpled.RobotsTxt shows both kinds; all from the stream without writing entries to disk.

**If it fails (no-go), change the plan:**
- Workflow's manifest is not in a direct dependency: Task 21 walks dependencies to depth 2, capped at 10 inspected nupkgs per package, and the cap is recorded in `docs/decisions.md`.
- A manifest lives at a path outside the patterns above: add the path to `NupkgInspector`'s pattern list in Task 19 and a synthetic-nupkg test for it.
- The legacy and new manifests can't be told apart for astroboard: H08 drops to `medium` and the astroboard golden expectation is re-confirmed with Pascal at the Fri 6 Nov review (Task 7).

- [ ] **Step 5: Checkpoint (Pascal commits):** `git add spikes/S2-nupkg-inspect docs/spikes.md tests/PackageCheck.Golden.Tests/golden-packages.json` / suggested message `spike: S2 nupkg inspection paths and golden hashes`

### Task 4: Spike S3, type-reference scan without loading (go/no-go)

**Files:**
- Create: `spikes/S3-type-refs/`, section `S3` in `docs/spikes.md`

**Interfaces:**
- Produces: whether H09 carries `high` weight (truth table rows 5 and 6) or `medium`.

- [ ] **Step 1: For each `lib/**/*.dll`** in SimpleDashboards 17.2.0 and `jcdcdev.Umbraco.Core` (at the version SimpleDashboards depends on), read the entry into a `MemoryStream`, open with `PEReader`/`MetadataReader`, and list `TypeReferences` whose namespace is `Umbraco.Cms.Infrastructure.Manifest` or starts with `Umbraco.Cms.Api.Management`.
- [ ] **Step 2: Repeat over the fixed control set** of 12 package ids, each at the version shown or, where none is shown, the latest stable whose `Umbraco.Cms*` range allows 17.7.2 (record the version used):
  1. `astroboard` 1.1.3 (known not working on 17)
  2. `Umbraco.Workflow` 17.5.4
  3. `Umbraco.Workflow.StaticAssets` (the version Workflow 17.5.4 resolves)
  4. `Crumpled.RobotsTxt` 3.1.1
  5. `MediaWiz.Forums` 14.0.0
  6. `Umbraco.Community.Contentment` 6.3.3
  7. `Skybrud.Umbraco.Redirects` 17.0.5
  8. `Our.Umbraco.GMaps` 17.3.1
  9. `Umbraco.Community.BlockPreview` 5.6.0
  10. `Umbraco.Forms`
  11. `uSync`
  12. `Umbraco.Commerce`

  Ids are as recalled; if one does not resolve on nuget.org, use the reserves in order (`Umbraco.Deploy`, `Diplo.GodMode`) and record the swap. **"Working" is defined objectively:** the package, or an Umbraco dependency within the S2 depth, ships an `umbraco-package.json`, or the package has no client assets at all (no `*.js`/`*.mjs`, no `package.manifest`). **"Not working"** means client assets or a `package.manifest` are present and no `umbraco-package.json` is. A **false positive** is a control package that H09 flags (references `IPackageManifestReader`/`PackageManifest`) and that is "not working". Record a table: id, version, working (yes/no and why), H09 flag.
- [ ] **Step 3: Assert no load:** after scanning, `AppDomain.CurrentDomain.GetAssemblies()` contains no assembly whose simple name matches any scanned DLL's name (deterministic; unrelated runtime loads don't matter).

**Pass criterion (go, H09 = high):** `jcdcdev.Umbraco.Core` (or SimpleDashboards) references `IPackageManifestReader` or `PackageManifest`; zero false positives in the 12-package control set; no scanned assembly loaded.

**If it fails (no-go), change the plan:**
- Found but with false positives: H09 weight becomes `medium` in Task 21. Truth table rows 5 and 6 then need H07 or H16, so SimpleDashboards becomes `ok` `medium`. That changes a design acceptance criterion; record it in `docs/truth-table-changes.md` and take it to the Fri 6 Nov review with Pascal (Task 7).
- Not found at all: H09 is dropped from 0.1 and listed under "Later"; same AC change, same review.
- Assembly set changed: stop. That is a Security failure; find the cause before any further task.

- [ ] **Step 4: Checkpoint (Pascal commits):** `git add spikes/S3-type-refs docs/spikes.md` / suggested message `spike: S3 metadata type-reference scan`

### Task 5: Spike S4, reading `.slnx` (go/no-go)

**Files:**
- Create: `spikes/S4-slnx/`, section `S4` in `docs/spikes.md`

- [ ] **Step 1: Find `.slnx` files:** `find <temp clones of public sample repos> -maxdepth 4 -name '*.slnx' -not -path '*/node_modules/*'`. Use public Umbraco sample repos cloned to a temp folder, never client sites. If only one real file exists, write a second by hand with nested solution folders and a project two folders deep.
- [ ] **Step 2: Read both with `Microsoft.VisualStudio.SolutionPersistence`** and with a 20-line `XDocument` reader (`//Project/@Path`). Read a real-world `.sln` from a public Umbraco sample repo with the library too. Read only; write nothing in that repo.
- [ ] **Step 3: Compare** project path lists. Check the library's licence and that it runs on `net8.0`.

**Pass criterion (go, use the library):** identical lists for both `.slnx` files, it reads the `.sln` too, MIT licence, `net8.0` compatible.

**If it fails (no-go):** Task 8 uses the `XDocument` reader for `.slnx` and a line parser for `.sln` (`Project("{...}") = "Name", "path", "{...}"`, skipping the solution-folder type GUID `2150E333-8FDC-42A3-9474-1A3956D46DE8`). The `SolutionFileReader` interface does not change.

- [ ] **Step 4: Checkpoint (Pascal commits):** `git add spikes/S4-slnx docs/spikes.md` / suggested message `spike: S4 slnx reader choice`

### Task 6: Spikes S5 and S6, Marketplace listing rules and freshness (go/no-go)

**Files:**
- Modify: `docs/spikes.md` (sections `S5`, `S6`)

- [ ] **Step 1 (S5):** From Marketplace docs and the API (`https://api.marketplace.umbraco.com/openapi/v1.json`, read-only GETs; never `validatePackage`), find: whether a NuGet package of type `DotnetTool` is listed, as type `Tool`; the required tag; the `umbraco-marketplace.json` fields and file location. Look at one existing listed tool package as an example. Tag each finding [verified] or [unverified].
- [ ] **Step 2 (S6, informational):** Check Umbraco17Check's latest release and README, any HQ upgrade-readiness tool, and new posts in forum thread 7933. Record the date, links, and whether any rival now inspects nupkg contents. S6 has no pass or fail; it only feeds README positioning.

**Pass criterion (go):** S5 names the tag and the JSON fields with a [verified] source. (S6 is informational and cannot fail the task.)

**If it fails (no-go), change the plan:**
- S5 finds tools are not listed: the M5 done-when becomes "on NuGet; Marketplace listing requested from HQ", and Task 33 drops `umbraco-marketplace.json`. Tell Pascal; it changes a milestone outcome.
- S5 cannot verify: Task 33 adds the tag and file per the best [unverified] reading, and Task 36 checks the listing appears within 24 h.
- S6 finds a rival inspecting nupkgs: no code change; Task 33's README positioning leads with CI exit codes, private feeds, overrides and multi-site (design section 11).

- [ ] **Step 3: Checkpoint (Pascal commits):** `git add docs/spikes.md` / suggested message `spike: S5 Marketplace listing and S6 freshness check`

### Task 7: M0 gate, the 4 golden verdicts print

**Files:**
- Create: `spikes/M0-golden/` (wires the S1-S3 spike code with a hand-coded copy of the design 7.4 truth table), `docs/truth-table-changes.md`

The gate prints **computed** verdicts: no override is applied (design section 8, Pascal 2026-10-30). Astroboard's seed override is a separate test (Task 24).

- [ ] **Step 1: Run the gate**

Run: `dotnet run --project spikes/M0-golden -- --target 17.7.2`
Expected, exactly:
```
astroboard 1.1.3 -> suspicious
Umbraco.Community.SimpleDashboards 17.2.0 -> ok
Umbraco.Workflow 17.5.4 -> ok
Umbraco.Community.Contentment 6.2.1 -> upgrade 6.3.3
```

- [ ] **Step 2: Record the unnamed goldens.** Also print, and record in `golden-packages.json` as `expected`, the verdicts for `Crumpled.RobotsTxt 3.1.1`, `MediaWiz.Forums 14.0.0`, Contentment `4.7.0` and `7.2.1`, `Our.Umbraco.Community.Contentment` (rename), Skybrud Redirects with and without `--prerelease`, astroboard 1.1.3 with `--no-inspect` (truth table row 9), and one NuGet-deprecated and one Marketplace-retired package (pick them here and record why).
- [ ] **Step 3: Results review with Pascal, Fri 6 Nov 2026.** Walk through `docs/spikes.md` (S1, S1b, S2, S3 table, S4, S5, S6) and the Step 2 list. Pascal confirms the Step 2 expectations and any `docs/truth-table-changes.md` entries. Record his decisions with the date in `docs/spikes.md`. Tasks 17 onwards wait for this review; Tasks 8-16 do not.

**Pass criterion (go):** the 4 lines above match exactly, and Pascal has confirmed the Step 2 expectations at the Fri 6 Nov review.

**If it fails (no-go):** per design 7.4, fix the table, not the fixture: write the changed row, the fixture that forced it and the reason in `docs/truth-table-changes.md`, and use the changed table in Task 17. If a fix would change any of the 4 design ACs, stop and ask Pascal.

- [ ] **Step 4: Checkpoint (Pascal commits):** `git add spikes/M0-golden docs/spikes.md docs/truth-table-changes.md tests/PackageCheck.Golden.Tests/golden-packages.json` / suggested message `spike: M0 gate, four golden verdicts`

---

## M1: inputs

### Task 8: Folder discovery and solution reading

**Files:**
- Create: `src/PackageCheck.Core/UsageException.cs` (holds the three exception types below)
- Create: `src/PackageCheck.Inputs/FolderDiscovery.cs`, `src/PackageCheck.Inputs/SolutionFileReader.cs`
- Create: `tests/Support/RepoFixtures/` (fixture folders written in this task and Task 11)
- Test: `tests/PackageCheck.Inputs.Tests/FolderDiscoveryTests.cs`, `SolutionFileReaderTests.cs`

**Interfaces:**
- Produces (Core): `class UsageException(string message) : Exception` (always exit 2); `class NoUmbracoProjectException(string path) : UsageException` with message `no Umbraco project found in {path}`; `class PathNotFoundException(string path) : UsageException` with message `path not found: {path}`. These are the only "no project" exception names in the plan.
- Produces: `static EntryPoint FolderDiscovery.FindEntry(string path)` where `record EntryPoint(EntryKind Kind, string Path)` and `enum EntryKind { Slnx, Sln, Csproj, ProjectSet }` (`ProjectSet` = a folder with only csproj files; `Path` is then the folder). Throws `PathNotFoundException` when `path` does not exist and `NoUmbracoProjectException` when the folder has no solution or csproj within the search rules.
- Produces: `static SolutionRead SolutionFileReader.Read(string solutionPath)` where `record SolutionRead(IReadOnlyList<string> ProjectPaths, IReadOnlyList<string> Warnings)`; paths absolute, normalised, `.csproj` only. A solution file that cannot be parsed returns no projects and one warning; it never throws.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Folder_PrefersSlnxThenSlnThenCsproj()   // fixture has a.slnx and b.sln at depth 2 -> Slnx
[Fact] public void Folder_SkipsBinObjNodeModulesGit()      // only csproj is under node_modules -> throws NoUmbracoProjectException
[Fact] public void Folder_SearchesAtMostFourLevels()       // sln at depth 5 is not found -> throws NoUmbracoProjectException
[Fact] public void MissingPath_ThrowsPathNotFound()        // message "path not found: <path>"
[Fact] public void Slnx_ListsProjectsInNestedFolders()
[Fact] public void Sln_SkipsSolutionFolders()
[Fact] public void MissingProjectInSolution_IsSkippedWithWarning()
{
    var r = SolutionFileReader.Read(Fixture("sln-missing-project/Site.sln"));
    Assert.Single(r.ProjectPaths);
    Assert.Contains(r.Warnings, w => w.Contains("Gone.csproj") && w.Contains("not found"));
}
[Fact] public void MalformedSlnx_WarnsAndReturnsNoProjects()  // truncated XML -> ProjectPaths empty, one warning naming the file
[Fact] public void MalformedSln_WarnsAndReturnsNoProjects()   // garbage text -> same
[Fact] public void Solution_SameProjectListedTwice_Once()     // ProjectPaths.Count == 1
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Inputs.Tests --filter "FolderDiscoveryTests|SolutionFileReaderTests"`. Expected: FAIL, types not found.
- [ ] **Step 3: Implement** the exceptions and both readers, using the S4 decision. Search order and skip list are design 6.1 "Path handling".
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src/PackageCheck.Core src/PackageCheck.Inputs tests/PackageCheck.Inputs.Tests tests/Support/RepoFixtures` / suggested message `feat(inputs): folder discovery and sln/slnx reading`

### Task 9: Project files, central package management and properties

**Files:**
- Create: `src/PackageCheck.Core/Model/PackageRef.cs`, `ProjectInfo.cs`, `ScanInput.cs`
- Create: `src/PackageCheck.Inputs/MsBuildProperties.cs`, `CentralPackageVersions.cs`, `ProjectFileReader.cs`
- Test: `tests/PackageCheck.Inputs.Tests/ProjectFileReaderTests.cs`, `CentralPackageVersionsTests.cs`

**Interfaces:**
- Produces (Core): `record PackageRef(string Id, string? VersionText, NuGetVersion? Version, bool Direct, string? UnresolvedReason)`; `record ProjectInfo(string Path, IReadOnlyList<string> Tfms, NuGetVersion? UmbracoVersion, IReadOnlyList<PackageRef> Packages)`; `record ScanInput(string SitePath, IReadOnlyList<ProjectInfo> Projects, IReadOnlyList<string> Warnings, TransitiveStatus Transitive)`; `enum TransitiveStatus { NotRequested, FromLockFile, FromMsBuild, Unavailable }`.
- Produces (Inputs): `static MsBuildProperties MsBuildProperties.ForProject(string csprojPath)` (merges `Directory.Build.props` files walking up to the repo root, then the csproj's own `PropertyGroup`s; conditions ignored); `string? Expand(string raw)` returns null when any `$(X)` stays unresolved.
- Produces: `static CentralPackageVersions? CentralPackageVersions.ForProject(string csprojPath, MsBuildProperties props)` (nearest `Directory.Packages.props` above, following `Import` of a parent props); `string? VersionFor(string id)`.
- Produces: `static ProjectInfo ProjectFileReader.Read(string csprojPath)`; `UmbracoVersion` is the highest version among `Umbraco.Cms` and `Umbraco.Cms.Core` refs.

- [ ] **Step 1: Write the failing tests** (fixtures as tiny XML files)

```csharp
[Fact] public void Cpm_VersionComesFromProps()
[Fact] public void Cpm_VersionOverrideWins()                  // VersionOverride="6.3.3" beats props 6.2.1
[Fact] public void Property_FromDirectoryBuildProps_IsExpanded() // $(UmbracoVersion) = 17.7.2
[Fact] public void UnresolvedProperty_IsUnknownNotCrash()
{
    var p = ProjectFileReader.Read(Fixture("unresolved-prop/Web/Web.csproj"));
    var r = p.Packages.Single(x => x.Id == "Our.Thing");
    Assert.Null(r.Version);
    Assert.Equal("unresolved MSBuild property", r.UnresolvedReason);
}
[Fact] public void FloatingVersion_WithoutLock_IsUnresolved()   // "17.*" -> UnresolvedReason "unresolved MSBuild property", VersionText "17.*"
[Fact] public void VersionAsChildElement_IsRead()             // <PackageReference><Version>1.2.3</Version>
[Fact] public void MultiTargeting_TfmsListed()                // <TargetFrameworks>net8.0;net10.0
[Fact] public void UmbracoVersion_IsHighestOfCmsAndCore()
[Fact] public void MalformedCsproj_IsSkippedWithWarning()     // ReadSafe returns null + warning; does not throw
[Fact] public void OpenRange_WithoutLock_IsUnresolved()       // "[13.0.0,14.0.0)" -> Version null, VersionText kept
[Fact] public void ExactRange_ResolvesToMinimum()             // "[17.7.2]" -> 17.7.2
[Fact] public void BareVersion_IsThatVersion()                // "6.2.1" -> 6.2.1 (NuGet's minimum-inclusive form)
[Fact] public void Cpm_MalformedPackagesProps_WarnsAndRefsUnresolved() // Version null, UnresolvedReason "unresolved MSBuild property", one warning naming the props file
[Fact] public void Cpm_DisabledByProperty_UsesCsprojVersions()  // ManagePackageVersionsCentrally=false
[Fact] public void Cpm_GlobalPackageReference_IsListed()       // <GlobalPackageReference> appears as a direct ref
[Fact] public void DuplicatePackageReference_InOneCsproj_FirstWinsWithWarning()
```
Exact ranges like `[17.7.2]` resolve to their minimum version; open or floating ranges are unresolved. A malformed input file is always a warning and a skip, never a crash (plan review P13).

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Inputs.Tests --filter "ProjectFileReaderTests|CentralPackageVersionsTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** with `XDocument` and `NuGet.Versioning` only (no MSBuild evaluation). Add `static (ProjectInfo? Project, string? Warning) ProjectFileReader.ReadSafe(string path)` for the malformed case.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src/PackageCheck.Core/Model src/PackageCheck.Inputs tests/PackageCheck.Inputs.Tests tests/Support/RepoFixtures` / suggested message `feat(inputs): csproj, CPM, VersionOverride and property expansion`

### Task 10: `packages.lock.json` v2 and `--transitive`

**Files:**
- Create: `src/PackageCheck.Inputs/LockFileReader.cs`
- Test: `tests/PackageCheck.Inputs.Tests/LockFileReaderTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<PackageRef> LockFileReader.Read(string lockPath, string tfm)`; maps `Direct` and `CentralTransitive`/`Transitive` types to `Direct` true/false; uses `resolved`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void LockV2_TransitiveUmbracoPackage_IsDirectFalse() // Umbraco.Workflow.Core listed, Direct == false
[Fact] public void LockFile_ResolvesFloatingVersion()              // csproj "17.*", lock resolved 17.7.2 -> Version 17.7.2
[Fact] public void LockV1_IsRejectedWithWarning()
[Fact] public void LockFile_PicksTfmMatchingProject()
[Fact] public void LockFile_Malformed_WarnsAndReturnsEmpty()        // no throw; warning names the file
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Inputs.Tests --filter LockFileReaderTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with `System.Text.Json`. The lock file's resolved version replaces an unresolved direct ref's version and clears `UnresolvedReason`.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src/PackageCheck.Inputs tests/PackageCheck.Inputs.Tests` / suggested message `feat(inputs): packages.lock.json v2 reader`

### Task 11: `ProjectReader`, site rules and the 10 repo-shape fixtures (M1 done)

**Files:**
- Create: `src/PackageCheck.Inputs/ProjectReader.cs`, `src/PackageCheck.Core/Model/SitePackage.cs`, `src/PackageCheck.Core/Model/InputOptions.cs`
- Create: `tests/Support/RepoFixtures/shape-01` ... `shape-10`, plus `tests/Support/RepoFixtures/README.md` listing each shape
- Test: `tests/PackageCheck.Inputs.Tests/RepoShapeTests.cs`, `ProjectReaderTests.cs`

**Interfaces:**
- Consumes: Tasks 8-10.
- Produces: `interface IProjectReader { Task<ScanInput> ReadAsync(string path, InputOptions o, CancellationToken ct); }`; `record InputOptions(bool Transitive, bool UseMsBuild)`.
- Produces: `static IReadOnlyList<SitePackage> SitePackages.From(ScanInput input)` where `record SitePackage(PackageRef Ref, IReadOnlyList<string> Projects)`; one entry per distinct (id case-insensitive, version or raw text).
- Produces: `NuGetVersion? ScanInput.SiteUmbracoVersion` (highest across projects; `null` when no project references `Umbraco.Cms` or `Umbraco.Cms.Core`) and the warning text `mixed Umbraco majors` when majors differ.
- `ReadAsync` throws `NoUmbracoProjectException` when `SiteUmbracoVersion` is null after reading, and `PathNotFoundException` for a missing path. The multi-path rule (warn and skip) lives in the orchestrator (Task 28a), not here.

The 10 shapes are synthetic: they copy the *shape* of the Manifesto v17 repos (csproj, props, lock skeletons with ids and versions), with no code, no `appsettings`, no client names (design section 9). Shapes: (1) `.slnx` + CPM; (2) `.sln` + CPM + `VersionOverride`; (3) plain csproj versions; (4) lock v2 with transitive Workflow; (5) test project outside `src/`; (6) site under `cms/`; (7) property from `Directory.Build.props`; (8) unresolved property; (9) mixed majors 13 and 17; (10) folder with no Umbraco project.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory, MemberData(nameof(Shapes))] public async Task Shape_MatchesExpected(string shape) // each fixture has expected.json: projects, ids, versions, direct, warnings
[Fact] public async Task NoUmbracoProject_ThrowsNoUmbracoProjectException()  // message "no Umbraco project found"
[Fact] public async Task TestProjectOutsideSrc_IsIncluded()
[Fact] public async Task MixedMajors_AddsSiteWarning_AndUsesHighest()
[Fact] public async Task Transitive_NoLockFile_IsUnavailable()   // ScanInput.Transitive == Unavailable, warning printed
[Fact] public void SamePackage_TwoVersions_TwoRows()            // Contentment 6.2.1 in Web, 4.7.0 in Tests -> 2 SitePackages, projects listed
[Fact] public void SamePackage_SameVersion_TwoProjects_OneRow() // Contentment 6.2.1 in Web and Core -> 1 SitePackage, Projects == [Core, Web]
[Fact] public void SamePackage_IdCaseDiffers_OneRow()           // "contentment" and "Contentment" at 6.2.1 -> 1 row, id as first written
[Fact] public void SiteUmbracoVersion_NullWhenNoUmbracoRef()
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Inputs.Tests --filter "RepoShapeTests|ProjectReaderTests"`. Expected: FAIL.
- [ ] **Step 3: Implement `ProjectReader : IProjectReader`.** With `Transitive` and a lock file next to a project, merge its refs; without one (and without `UseMsBuild`), set `TransitiveStatus.Unavailable`.
- [ ] **Step 4: Run all Inputs tests.** Run: `dotnet test tests/PackageCheck.Inputs.Tests`. Expected: `Failed: 0`, and `RepoShapeTests` shows 10 passed cases.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(inputs): project reader, site rules and ten repo-shape fixtures`

---

## M2: metadata verdicts

### Task 12: NuGet metadata source, recorded HTTP and source errors

**Files:**
- Create: `src/PackageCheck.Core/Facts/IPackageMetadataSource.cs`, `PackageVersions.cs`, `PackageDetails.cs`, `PackageSourceException.cs`, `src/PackageCheck.Core/Model/UnknownReason.cs`
- Create: `src/PackageCheck.Sources/NuGet/NuGetMetadataSource.cs`, `FeedSettings.cs`, `SourceErrorClassifier.cs`, `src/PackageCheck.Sources/Http/HttpPolicy.cs`
- Create: `tests/Support/FixtureHttpHandler.cs`, `tests/Support/Http/nuget/*.json`
- Test: `tests/PackageCheck.Sources.Tests/NuGetMetadataSourceTests.cs`, `SourceErrorClassifierTests.cs`

**Interfaces:**
- Produces (Core):
  - `interface IPackageMetadataSource { Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct); Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct); Task<NupkgHandle?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct); }`
  - `record PackageVersions(string Id, IReadOnlyList<NuGetVersion> Versions)` (listed and unlisted, sorted ascending)
  - `record PackageDetails(PackageIdentity Identity, IReadOnlyList<PackageDependencyGroup> DependencyGroups, DeprecationInfo? Deprecation, DateTimeOffset? Published, bool Listed)`
  - `record DeprecationInfo(IReadOnlyList<string> Reasons, string? AlternateId, VersionRange? AlternateRange)`
  - `sealed record NupkgHandle(string FilePath, bool IsTemporary, long Length) : IAsyncDisposable` (deletes the file when temporary)
  - `enum UnknownReason { Offline, UnresolvedProperty, PrivateFeed, NotFound, UnsafeArchive, ConflictingEvidence, SourceError }`
  - `class PackageSourceException(UnknownReason reason, string safeMessage) : Exception`
  - `class NupkgTooLargeException(string id, NuGetVersion v, long limit) : Exception` (caught by `FactsCollector`, which falls back to metadata-only evidence)
- Produces (Sources): `NuGetMetadataSource(FeedSettings feeds, HttpPolicy policy, HttpMessageHandler? testHandler)`; `static FeedSettings FeedSettings.ForSite(string sitePath)` via `Settings.LoadDefaultSettings(sitePath)` and source mapping; `static UnknownReason SourceErrorClassifier.Classify(Exception e)`; `static string FeedSettings.Redact(string url)` (strips userinfo and query).
- Produces (Sources): `HttpPolicy(TimeProvider clock, int maxTries = 3, TimeSpan? perCallTimeout = null)` (30 s default); backoff delays go through `clock`, so tests use `FakeTimeProvider` and never sleep (plan review P14).
- Produces (tests): `FixtureHttpHandler(string folder, RecordMode mode)` replays `METHOD url -> status, body` files; `PACKAGECHECK_RECORD=1` records live responses with `Authorization` headers and userinfo stripped. `int RequestCount`; `int MaxInFlight` (highest concurrent requests seen, used by Task 28a).

`OpenNupkgAsync` order (design 7.5): global packages folder (`SettingsUtility.GetGlobalPackagesFolder`, honours `NUGET_PACKAGES`), else download to a temp file through a stream that throws `NupkgTooLargeException` at 250 MB. Global-folder files over the cap are not opened. Returning a `NupkgHandle` (path, temporary flag, length) instead of the design 7.2 sketch's `Stream` is a deliberate deviation: the inspector needs a seekable file for `ZipArchive`, and the handle owns temp-file deletion. Record it in `docs/decisions.md` in Step 3.

**Credentials:** per S1b (Task 2b), either set up NuGet's credential service with `nonInteractive: true` or use `NuGet.config` credentials only. Either way, nothing in the tool can prompt.

**Multi-feed outcomes** (design 7.3 applied per package across every feed the source mapping allows; the first feed that returns the package wins):

| Feed A | Feed B | Outcome |
|---|---|---|
| 404 | 401 or 403 | `unknown`, `private-feed` |
| 404 | 200 | found on B |
| 401 | 200 | found on B; no warning |
| 5xx after retries, or connection refused | 200 | found on B |
| 5xx after retries | 404 | `unknown`, `source-error` (a feed that might hold it failed) |
| 404 | 404 | `unknown`, `not-found` |

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Versions_IncludeUnlisted()
[Fact] public async Task Details_HaveDependencyGroupsPerTfm()          // Contentment 6.2.1 has a group with Umbraco.Cms.* range
[Fact] public async Task Details_DeprecationWithAlternate()
[Fact] public async Task NotFoundOnAllFeeds_ThrowsNotFound()           // Reason == NotFound
[Fact] public async Task Feed401_ThrowsPrivateFeed()
[Fact] public async Task Feed403_ThrowsPrivateFeed()
[Fact] public async Task ConnectionRefused_ThrowsSourceError()          // handler throws HttpRequestException(SocketException ConnectionRefused)
[Theory] [InlineData("404","401","private-feed")] [InlineData("404","200","found")] [InlineData("401","200","found")]
         [InlineData("5xx","200","found")] [InlineData("refused","200","found")] [InlineData("5xx","404","source-error")] [InlineData("404","404","not-found")]
public async Task MixedFeeds_Matrix(string feedA, string feedB, string expected) // two fixture feeds; table above
[Fact] public async Task NearestNuGetConfig_Wins()                     // site/NuGet.config and site/src/NuGet.config; project under src uses the nearer one's feeds
[Fact] public async Task SourceMapping_UnmappedFeed_NeverQueried()     // RequestCount on the dummy feed == 0
[Fact] public async Task Timeout_And_5xxAfterRetries_ThrowSourceError()  // handler sees 3 attempts; FakeTimeProvider advanced, no real sleep
[Fact] public async Task Nupkg_GlobalFolderFirst_NoHttp()               // RequestCount == 0
[Fact] public async Task Nupkg_OverCap_ThrowsTooLarge_AndTempDeleted()
[Fact] public async Task Credentials_NeverInExceptionMessages()         // NuGet.config with ClearTextPassword "PLANTED-TOKEN-123"; feed returns 401; message lacks the token
[Fact] public void UserAgent_IsSet()                                    // "umbraco-package-check/" prefix
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Sources.Tests --filter "NuGetMetadataSourceTests|SourceErrorClassifierTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** on NuGet.Protocol (or the S1 fallback). Inject `testHandler` through the S1 seam. Set the user agent with `UserAgent.SetUserAgentString(new UserAgentStringBuilder("umbraco-package-check"))`. Retries and timeout come from `HttpPolicy` (3 tries, backoff 1 s/2 s/4 s with jitter, 30 s per call). Credentials per S1b. Record the `NupkgHandle` deviation in `docs/decisions.md`.
- [ ] **Step 4: Record fixtures** once with `PACKAGECHECK_RECORD=1 dotnet test --filter NuGetMetadataSourceTests` for the golden ids, then run without it. Expected: `Failed: 0`. Check `grep -ri "authorization\|password" tests/Support/Http` prints nothing.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(sources): NuGet metadata source with recorded HTTP fixtures`

### Task 13: Seed data and target resolution

**Files:**
- Create: `data/umbraco-versions.json`, `data/overrides.seed.json` (embedded resources in `PackageCheck.Sources`)
- Create: `src/PackageCheck.Core/Targets/TargetVersion.cs`, `TargetResolver.cs`, `UmbracoVersionsTable.cs`
- Test: `tests/PackageCheck.Core.Tests/TargetResolverTests.cs`, `tests/PackageCheck.Core.Tests/NoHardCodedMajorTests.cs`

**Interfaces:**
- Consumes: `UsageException` (Task 8).
- Produces: `record TargetVersion(string Requested, NuGetVersion Resolved, NuGetFramework Tfm, bool Prerelease) { NuGetVersion RangeVersion => Prerelease ? new NuGetVersion(Resolved.Major, Resolved.Minor, Resolved.Patch) : Resolved; }`. Every range check (H01, H02, H03, H19, the upgrade walk) uses `RangeVersion` (design 6.1, Pascal 2026-10-30: `18.0.0-rc2` counts as `18.0.0`).
- Produces: `record UmbracoMajor(int Major, bool Lts, DateOnly Eol, string Tfm)`; `UmbracoVersionsTable(IReadOnlyList<UmbracoMajor>)` with `int LatestLtsMajor(DateOnly today)` and `NuGetFramework? FallbackTfm(int major)`.
- Produces: `TargetResolver(IPackageMetadataSource source, UmbracoVersionsTable table)`, `Task<TargetVersion> ResolveAsync(string requested, NuGetVersion? siteVersion, bool allowPrerelease, CancellationToken ct)`; throws `UsageException` (exit 2) with the messages below.

`umbraco-versions.json` holds majors 13 to 18 with `lts`, `eol` and `tfm`. Fill values from Umbraco's release calendar at the time of writing and tag the commit message with the date checked.

- [ ] **Step 1: Write the failing tests** (Umbraco.Cms versions served by a fake source)

```csharp
[Fact] public async Task Major_ResolvesLatestStableInMajor()           // "17" -> 17.7.2 when feed has 17.7.2 and 18.0.0-rc1
[Fact] public async Task Exact_ResolvesExactly()                        // "17.7.2"
[Fact] public async Task Current_UsesSiteVersion()
[Fact] public async Task NextMajor_IsSiteMajorPlusOne()                // site 17.7.2 -> latest stable 18.x
[Fact] public async Task NextMajor_NoneReleased_Throws()               // message contains "no 19.x release"
[Fact] public async Task Major_OnlyPrerelease_WithoutFlag_Throws()      // "no stable 18.x yet; pass --prerelease or an exact version"
[Fact] public async Task Major_OnlyPrerelease_WithFlag_ResolvesAndMarks() // Prerelease == true
[Fact] public async Task ExplicitPrerelease_AlwaysAllowed()            // "18.0.0-rc2" -> Resolved 18.0.0-rc2, Prerelease true
[Fact] public async Task PrereleaseTarget_RangeVersionIsReleaseBase()  // "18.0.0-rc2" -> RangeVersion 18.0.0; "17.7.2" -> RangeVersion 17.7.2
[Fact] public async Task LatestLts_FromTable()
[Fact] public async Task Tfm_FromUmbracoCmsNuspec_ThenFallbackTable()  // offline source -> table tfm
[Fact] public void NoLiteralUmbracoMajorsInSrc()                       // regex over src/**/*.cs for version comparisons against 13-19 literals; allowlist: the H03 "14" boundary constant, documented
```
The H03/H08 "14+" boundary is the new-backoffice cut-over, a fact about Umbraco history, not a target major. It lives in one named constant `UmbracoFacts.NewBackofficeMajor = 14` with a comment linking design 7.4.

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Core.Tests --filter "TargetResolverTests|NoHardCodedMajorTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Per-site resolution of `current` and `next-major` is the caller's job (Task 28a) by passing each site's version.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add data src tests` / suggested message `feat(core): target resolution and seed Umbraco versions table`

### Task 14: Facts collection and H04 (path to `Umbraco.Cms.*`)

**Files:**
- Create: `src/PackageCheck.Core/Facts/PackageFacts.cs`, `FactsCollector.cs`, `UmbracoDependency.cs`
- Create: `src/PackageCheck.Core/Facts/InspectionResult.cs`, `MarketplaceInfo.cs` (records only, exact shapes in Tasks 19 and 16; created here because `PackageFacts` holds them)
- Create: `src/PackageCheck.Core/Heuristics/IHeuristic.cs`, `Evidence.cs`, `H04UmbracoPath.cs`
- Test: `tests/PackageCheck.Core.Tests/FactsCollectorTests.cs`, `Heuristics/H04Tests.cs`

**Interfaces:**
- Produces:
  - `record UmbracoDependency(string ViaPackageId, string UmbracoPackageId, VersionRange Range, int Depth)` (Depth 0 = direct)
  - `record PackageFacts(string Id, NuGetVersion Version, PackageDetails Details, NuGetFramework? ChosenGroupTfm, UmbracoDependency? Umbraco, IReadOnlyList<string> DependencyIds, InspectionResult? Own, IReadOnlyList<InspectionResult> Dependencies, MarketplaceInfo? Marketplace, IReadOnlyList<string> ProjectDirectories, IReadOnlyList<NuGetVersion> AllVersions) { bool ContentsKnown => Own is not null; bool IsServerOnly; }` (inspection and Marketplace fields stay null until Tasks 16 and 21; `IsServerOnly` is true only when contents are known and own plus dependencies have no client assets, no manifest and no Management API refs)
  - `enum EvidenceResult { Positive, Negative, Info }`; `enum EvidenceWeight { High, Medium, Low, Info }`
  - `record Evidence(string Check, EvidenceResult Result, EvidenceWeight Weight, string Source, string Detail)`
  - `interface IHeuristic { string Id { get; } bool AppliesTo(TargetVersion t); IEnumerable<Evidence> Evaluate(PackageFacts f, TargetVersion t); }`
  - `FactsCollector(IPackageMetadataSource meta)`, `Task<PackageFacts> CollectAsync(string id, NuGetVersion v, TargetVersion t, FactsOptions o, CancellationToken ct)`; `record FactsOptions(bool Inspect, IReadOnlyList<string> ProjectDirectories)`. Later tasks append optional constructor parameters (see "Interface order").
  - `InspectionResult` and `MarketplaceInfo` records with the exact fields given in Tasks 19 and 16.

Heuristics are synchronous and pure over `PackageFacts`; all I/O is in `FactsCollector`. This refines the design's async `IHeuristic` sketch so the truth table tests need no fakes (design 7.2 says names are the builder's to refine). Record it in `docs/decisions.md`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task DirectUmbracoDependency_Depth0()
[Fact] public async Task TransitiveUmbracoDependency_FoundAtDepth2()   // A -> jcdcdev.Umbraco.Core -> Umbraco.Cms.Core
[Fact] public async Task NoPathWithinDepth3_UmbracoIsNull()
[Fact] public async Task GroupChosen_IsNearestForTargetTfm()            // net10.0 target picks net10.0 group over net8.0
[Fact] public void H04_NoPath_EmitsNotUmbracoInfo()
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Core.Tests --filter "FactsCollectorTests|H04Tests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** The walk uses the nearest group at each level and takes each dependency's lowest allowed version. Cache details per (id, version) for the run.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(core): facts collection and H04 Umbraco path walk`

### Task 15: Metadata heuristics H01, H02, H03, H05, H14, H15, H18

**Files:**
- Create: `src/PackageCheck.Core/Heuristics/H01RangeAllows.cs`, `H02UpperBoundExcludes.cs`, `H03OpenRangeFromOldMajor.cs`, `H05RemovedCorePackage.cs`, `H14NuGetDeprecation.cs`, `H15PrereleaseOnly.cs`, `H18StaleRelease.cs`, `UmbracoFacts.cs`
- Test: `tests/PackageCheck.Core.Tests/Heuristics/H01Tests.cs` ... one class per heuristic

**Interfaces:**
- Consumes: `PackageFacts`, `TargetVersion`, `IHeuristic` (Task 14).
- Produces: the check ids `"H01"` etc. used by the truth table and the report. `UmbracoFacts.RemovedBackofficePackages = ["Umbraco.Cms.Web.BackOffice"]`, the one package the design names. Add a further id only with a cited line from the Umbraco 14.0 release notes or breaking-changes page, in a comment beside it; the test pins the final list.
- H01, H02, H03 and H19 compare ranges with `TargetVersion.RangeVersion` (Task 13).
- H15 reads `PackageFacts.AllVersions`.

| Heuristic | Evidence emitted (Result, Weight) |
|---|---|
| H01 | allows: Positive, Medium if bounded, Low if open |
| H02 | upper bound excludes target: Negative, High |
| H03 | open range, min below 14, target 14+: Negative, Low, detail `open range >= {min}` |
| H05 | depends on a removed backoffice package: Negative, High |
| H14 | NuGet deprecation: Negative, High, detail names alternate |
| H15 | only a prerelease supports the target: Info |
| H18 | last release published before the target major's first release: Info |

- [ ] **Step 1: Write the failing tests**, one per row plus the non-firing case, using the spec's values, for example:

```csharp
[Fact] public void H03_Astroboard_OpenRangeFrom13_8_1()
{
    var ev = new H03OpenRangeFromOldMajor().Evaluate(Facts("astroboard", "1.1.3", range: ">= 13.8.1"), Target("17.7.2", "net10.0")).Single();
    Assert.Equal(("H03", EvidenceResult.Negative, EvidenceWeight.Low), (ev.Check, ev.Result, ev.Weight));
    Assert.Equal("open range >= 13.8.1", ev.Detail);
}
[Fact] public void H03_SkippedForTarget13()            // AppliesTo == false
[Fact] public void H01_Bounded_IsMedium() [Fact] public void H01_Open_IsLow()
[Fact] public void H02_Contentment621_ExcludesTarget()
[Fact] public void H01_PrereleaseTarget_ComparesReleaseBase()   // range "[18.0.0, )", target 18.0.0-rc2 -> H01 Positive (plain NuGet would exclude it)
[Fact] public void H02_PrereleaseTarget_UpperBoundAtBase_Excludes() // range "[17.0.0, 18.0.0)", target 18.0.0-rc2 -> H02 Negative High
[Fact] public void H05_RemovedPackages_ListIsPinned()           // Assert.Equal(["Umbraco.Cms.Web.BackOffice", ...as recorded], UmbracoFacts.RemovedBackofficePackages)
[Fact] public void H05_DependsOnWebBackOffice_IsHighNegative()
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Core.Tests --filter "FullyQualifiedName~Heuristics"`. Expected: FAIL.
- [ ] **Step 3: Implement** each class; `AppliesTo` returns false for H03 when the target major is below `UmbracoFacts.NewBackofficeMajor`.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(core): metadata heuristics H01-H03, H05, H14, H15, H18`

### Task 16: Marketplace source and H12, H13, H19

**Files:**
- Create: `src/PackageCheck.Core/Facts/IMarketplaceSource.cs`, `MarketplaceInfo.cs`
- Create: `src/PackageCheck.Sources/Marketplace/MarketplaceClient.cs`, `MarketplaceUrls.cs`
- Create: `src/PackageCheck.Core/Heuristics/H12MarketplaceLatest.cs`, `H13MarketplaceRetired.cs`, `H19HqStrictRange.cs`
- Test: `tests/PackageCheck.Sources.Tests/MarketplaceClientTests.cs`, `tests/PackageCheck.Core.Tests/Heuristics/H12Tests.cs`, `H13Tests.cs`, `H19Tests.cs`

**Interfaces:**
- Consumes: `MarketplaceInfo` (record created in Task 14).
- Produces: `interface IMarketplaceSource { Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct); }`; the record's fields are `record MarketplaceInfo(string Url, bool IsRetired, string? AlternatePackageId, bool IsHq, NuGetVersion? LatestForTarget, string? PackageIdForTarget)`.
- Produces: `FactsCollector(IPackageMetadataSource meta, IMarketplaceSource? marketplace = null)`.
- Produces: `static Uri MarketplaceUrls.PackageDetail(string id, NuGetVersion target)` = `https://api.marketplace.umbraco.com/api/v1.0/packages/{id}?SupportsUmbracoVersionNumber={target}`. There is no other URL builder in the class.
- Produces: `MarketplaceClient(HttpClient http)` returns null on any error (logged as a warning), never throws `PackageSourceException`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Detail_ParsesRetiredAlternateAndHq()
[Fact] public async Task AnyError_ReturnsNull_NoThrow()              // 500, timeout, malformed JSON
[Fact] public async Task NeverCallsValidatePackage()                // run GetAsync for 5 ids; handler saw no URL containing "validatePackage"
[Fact] public void SourceHasNoValidatePackageString()               // grep src/ for "validatePackage" == 0 hits
[Fact] public void H13_Retired_IsHighNegativeWithAlternate()
[Fact] public void H19_HqWithStrictRange_RaisesH01ToHigh()
[Fact] public void H12_IsInfoOnly()                                  // Weight Info; Marketplace is not independent evidence
```

- [ ] **Step 2: Run** `dotnet test --filter "MarketplaceClientTests|H12Tests|H13Tests|H19Tests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Escape the id with `Uri.EscapeDataString`. `FactsCollector` fills `PackageFacts.Marketplace`.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: Marketplace source and H12, H13, H19`

### Task 17: Truth table, verdict engine and upgrade selection

**Files:**
- Create: `src/PackageCheck.Core/Model/Verdict.cs`, `Confidence.cs`, `PackageVerdict.cs`
- Create: `src/PackageCheck.Core/Overrides/OverrideEntry.cs`, `OverrideSet.cs`, `OverrideApplied.cs` (records and enums only; Task 18 adds the logic)
- Create: `src/PackageCheck.Core/Verdicts/TruthTable.cs`, `UpgradeSelector.cs`, `VerdictEngine.cs`
- Test: `tests/PackageCheck.Core.Tests/Verdicts/TruthTableTests.cs`, `UpgradeSelectorTests.cs`, `VerdictEngineTests.cs`

**Interfaces:**
- Produces: `enum Verdict { Ok, Upgrade, NoRelease, Suspicious, Replaced, Unknown, NotUmbraco }`; `enum Confidence { High, Medium, Low }`.
- Produces (override model, moved here from Task 18 for compile order): `enum OverrideStatus { Works, Broken, Partial }`; `enum OverrideOrigin { Community, Local }`; `record OverrideEntry(string Id, VersionRange PackageVersions, VersionRange Umbraco, OverrideStatus Status, string Summary, Uri Evidence, string? ReportedBy, DateOnly VerifiedOn, DateOnly ReviewBy, string? Replacement, OverrideOrigin Origin)`; `record OverrideSet(IReadOnlyList<OverrideEntry> Entries, string? SourceLabel) { static OverrideSet Empty { get; } }`; `record OverrideApplied(OverrideEntry Entry, Verdict ComputedVerdict, bool Stale, string Label)`.
- Produces: `record TableOutcome(int Row, Verdict Verdict, Confidence? Confidence, UnknownReason? Reason) { bool Clears => Row is >= 5 and <= 8; }`; `static TableOutcome TruthTable.Decide(IReadOnlyList<Evidence> evidence, PackageFacts facts)` (uses `facts.ContentsKnown` and `facts.IsServerOnly` for the "n/a" contents column). Row 1 returns `Verdict.Upgrade` as a placeholder that the engine resolves to `upgrade` or `no-release`.
- Produces: `record UpgradeChoice(NuGetVersion? Recommended, NuGetVersion? HighestCompatible, NuGetVersion? PrereleaseCompatible)`; `static async Task<UpgradeChoice> UpgradeSelector.SelectAsync(NuGetVersion current, IReadOnlyList<NuGetVersion> all, Func<NuGetVersion, Task<bool>> metadataClears, Func<NuGetVersion, Task<bool>> fullyClears, bool allowPrerelease)`.
- Produces: `record PackageVerdict(SitePackage Package, Verdict Verdict, Confidence? Confidence, UnknownReason? Reason, string? Recommended, string? HighestCompatible, string? PrereleaseCompatible, IReadOnlyList<Evidence> Evidence, MarketplaceInfo? Marketplace, OverrideApplied? Override)`.
- Produces: `interface IVerdictEngine { Task<PackageVerdict> EvaluateAsync(SitePackage pkg, TargetVersion target, ScanOptions o, CancellationToken ct); }`; `record ScanOptions(bool Inspect, bool AllowPrerelease, bool Verbose)`; `VerdictEngine(FactsCollector facts, OverrideSet overrides, TimeProvider clock)`. In this task the engine ignores `overrides`; Task 18 wires it. Engine tests in this task and the golden tests (Task 24) pass `OverrideSet.Empty`.

Truth table: design 7.4 rows 1-10 verbatim, with any changes recorded in `docs/truth-table-changes.md` at Task 7. H13/H14 turn any row into `replaced` (high) unless the row is 1 or 5-8, where they add a note. Conflicting high evidence gives `unknown`, `conflicting-evidence`. Server-only packages (no client assets, no manifest, no Management API refs) are never `suspicious` from missing H07/H09.

Upgrade selection algorithm (the design fixes the rule, not the walk):
```
candidates = all versions > current, stable unless allowPrerelease, grouped by major ascending
for each major group: for v in group descending:
    if metadataClears(v): if fullyClears(v): Recommended = v; stop all groups
                          else: inspections++ ; if inspections == 3: stop
HighestCompatible = highest v in all groups with metadataClears(v), if != Recommended
PrereleaseCompatible = lowest prerelease v with metadataClears(v), when no stable clears and !allowPrerelease
```
`fullyClears` inspects (design: "inspect only the chosen candidate"); the 3-inspection cap bounds downloads when a candidate's contents disagree with its range. Record the cap in `docs/decisions.md`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory, MemberData(nameof(TableRows))] public void Row_N(int row, Evidence[] ev, Verdict v, Confidence? c) // all 10 rows, first-match order
[Fact] public void HighPositiveAndHighNegative_IsUnknownConflicting()
[Fact] public void H14WithRow7_AddsNoteOnly()  [Fact] public void H14WithRow3_IsReplacedHigh()
[Fact] public void ServerOnlyPackage_MissingH07H09_IsNotSuspicious()   // row 7 or 8 -> Ok, never Suspicious
[Fact] public async Task PrereleaseTarget_UsesReleaseBase_EndToEnd()  // engine, fake source with "[18.0.0, )", target "18.0.0-rc2" -> Ok, not Upgrade
[Fact] public async Task Contentment621_Target1772_UpgradeTo633_Not7() // Recommended "6.3.3", HighestCompatible "7.2.1"
[Fact] public async Task Skybrud_OnlyBetaClears_NoReleaseMentionsPrerelease() // Verdict NoRelease, PrereleaseCompatible "17.1.0-beta"
[Fact] public async Task Skybrud_WithPrerelease_UpgradeToBeta()
[Fact] public async Task DeprecatedWithAlternate_IsReplacedNamingAlternate()   // Recommended == alternate id
[Fact] public async Task SourceError_IsUnknownSourceError()                   // PackageSourceException(SourceError) -> Unknown, Reason SourceError
[Fact] public async Task UnresolvedRef_IsUnknownUnresolvedProperty_NoHttp()
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Core.Tests --filter "FullyQualifiedName~Verdicts"`. Expected: FAIL.
- [ ] **Step 3: Implement** the three types. The engine catches `PackageSourceException` per package and returns `unknown` with its reason.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(core): truth table, verdict engine and upgrade selection`

### Task 18: Overrides (H16): model, loading, matching and applying (M2 done)

**Files:**
- Create: `src/PackageCheck.Core/Overrides/OverrideApplier.cs`, `IOverrideSource.cs`
- Create: `src/PackageCheck.Sources/Overrides/OverrideFileParser.cs`, `BundledOverrideSource.cs`, `LocalOverrideSource.cs`, `CompositeOverrideSource.cs`
- Test: `tests/PackageCheck.Core.Tests/Overrides/OverrideApplierTests.cs`, `tests/PackageCheck.Sources.Tests/OverrideFileParserTests.cs`, `LocalOverrideSourceTests.cs`

**Interfaces:**
- Consumes: the override model records (Task 17).
- Produces: `interface IOverrideSource { Task<OverrideSet> LoadAsync(CancellationToken ct); }`.
- `OverrideApplied.Label` is `"override"` or `"local override"`.
- Produces: `static PackageVerdict OverrideApplier.Apply(PackageVerdict computed, OverrideSet set, TargetVersion target, DateOnly today, bool anotherVersionClears)`. Synchronous; the engine computes `anotherVersionClears` (the upgrade walk found a `Recommended`) before calling (plan review P10). Matching uses `target.RangeVersion` against `Umbraco`.
- Produces: `static bool OverrideApplier.BlocksCandidate(OverrideSet set, string id, NuGetVersion candidate, TargetVersion target)`: true when a `broken` or `partial` entry matches that candidate. The upgrade walk treats a blocked candidate as not clearing.
- Produces: `static OverrideSet OverrideFileParser.Parse(string content, string fileNameOrUrl, OverrideOrigin origin)` accepting the YAML entry format of design 7.8 or JSON. `reviewBy` defaults to `verifiedOn` + 12 months. `evidence` must be an absolute `https` URL or the entry is rejected with a warning.
- Produces: `BundledOverrideSource()` reads the embedded `overrides.seed.json` (labels its entries `Community`); `CompositeOverrideSource(IReadOnlyList<IOverrideSource> sources)` merges in order, later sources winning for the same id and range.
- Produces: `LocalOverrideSource(string fileOrUrl, HttpClient http)`: a path is read from disk; an `https://` URL is fetched once (30 s timeout); any other scheme is a `UsageException` (exit 2). A fetch failure is a warning and an empty local set, never exit 3.

Mapping (design 7.8): `works` → `ok` high; `broken` → `upgrade` if `anotherVersionClears`, else `no-release`; `partial` → `suspicious` with the summary. `replacement`, when set, is shown in `override.replacement` and becomes the console action "Replace X with Y"; it does not change the mapped verdict. Local beats community for the same id and range.

**Upgrade candidates (architect's call, plan review P11):** overrides decide the verdict of the version the site uses. For candidates, only `broken` and `partial` entries count, and only to block a candidate the override says is bad; a `works` entry never makes a candidate clear. This keeps the tool from recommending a version the community has marked broken, without letting curated data stand in for range evidence.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Match_ReplacesVerdict_KeepsComputed_AndLabels()   // astroboard seed: computed Suspicious -> NoRelease, Override.ComputedVerdict == Suspicious, Label "override"
[Fact] public void Match_BeatsConflictingEvidenceUnknown()
[Fact] public void PastReviewBy_StillApplies_MarkedStale()
[Fact] public void LocalBeatsCommunity()                             // Label "local override"
[Fact] public void IdMatch_IsCaseInsensitive()
[Fact] public void NoMatch_OutsideUmbracoRange()
[Fact] public void Parse_Yaml_And_Json_Equivalent()
[Fact] public void Parse_NonHttpsEvidence_Rejected()
[Fact] public void Works_IsOkHigh_KeepsComputed()                    // computed Suspicious -> Ok, Confidence High, ComputedVerdict Suspicious
[Fact] public void Partial_IsSuspiciousWithSummary()                 // computed Ok -> Suspicious; evidence detail == entry summary
[Fact] public void Broken_AnotherVersionClears_IsUpgrade()          // anotherVersionClears: true -> Upgrade
[Fact] public void Broken_NothingClears_IsNoRelease()
[Fact] public void NoMatch_PackageVersionsExcludeCurrent()           // entry "[2.0.0, )", site on 1.1.3 -> verdict unchanged, Override null
[Fact] public void Replacement_IsCarriedOnOverride()                 // Override.Entry.Replacement == "New.Package"; verdict per status mapping
[Fact] public void BrokenEntry_BlocksMatchingCandidate()             // BlocksCandidate true for 1.2.0 under "[1.0.0, )"
[Fact] public void WorksEntry_NeverBlocksOrClearsCandidate()         // BlocksCandidate false
[Fact] public async Task Local_HttpsUrl_IsFetched()                  // FixtureHttpHandler serves YAML; entries Origin Local
[Fact] public void Local_HttpUrl_IsUsageError()                      // "http://..." -> UsageException
[Fact] public async Task Local_FetchFails_WarnsAndEmpty()            // 500 -> empty set, one warning
```

- [ ] **Step 2: Run** `dotnet test --filter "OverrideApplierTests|OverrideFileParserTests|LocalOverrideSourceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** `data/overrides.seed.json` holds the astroboard entry from design 7.8 with a real public evidence link (forum thread 7933 post), plus `umbraco-versions.json` embedded alongside. The engine applies overrides last (design 7.2 step 6) and passes `BlocksCandidate` into the upgrade walk's `metadataClears`.
- [ ] **Step 4: Run the whole M2 set.** Run: `dotnet test --filter "Category!=Golden"`. Expected: `Failed: 0`, including the M2 AC tests from Tasks 17 and 18.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add data src tests` / suggested message `feat: overrides model, seed snapshot and H16`

---

## M3: inspection, cache, offline

### Task 19: Archive guard and nupkg inspector

**Files:**
- Create: `src/PackageCheck.Core/Facts/INupkgInspector.cs`
- Create: `src/PackageCheck.Inspect/ArchiveGuard.cs`, `ArchiveLimits.cs`, `CountingReadStream.cs`, `NupkgInspector.cs`, `StaticWebAssetsProps.cs`
- Create: `tests/Support/NupkgBuilder.cs`
- Test: `tests/PackageCheck.Inspect.Tests/NupkgInspectorTests.cs`, `ArchiveGuardTests.cs`, `CountingReadStreamTests.cs`

**Interfaces:**
- Consumes: `InspectionResult` (record created in Task 14) with exactly these fields: `record InspectionResult(string PackageId, string Version, bool HasUmbracoPackageJson, string? StaticWebAssetBasePath, bool HasLegacyManifest, bool ReferencesManifestReader, bool ReferencesManagementApi, bool HasClientAssets, IReadOnlyList<string> LibTfms, string ContentHash, IReadOnlyList<string> ManifestPaths)`. The extra fields over the design 7.2 sketch (`PackageId`, `Version`, `ManifestPaths`) are recorded in `docs/decisions.md`.
- Produces: `interface INupkgInspector { Task<InspectionResult> InspectAsync(PackageIdentity id, NupkgHandle nupkg, CancellationToken ct); }`.
- Produces: `record ArchiveLimits(long MaxNupkgBytes = 250L*1024*1024, int MaxEntries = 20_000, long MaxEntryBytes = 50L*1024*1024, long MaxTotalBytes = 500L*1024*1024) { static ArchiveLimits Default { get; } }`; `static void ArchiveGuard.Check(ZipArchive zip, ArchiveLimits l)` throws `PackageSourceException(UnsafeArchive, ...)`.
- Produces: `sealed class ByteBudget(long limit) { void Spend(long bytes); }` (throws the total-cap `PackageSourceException` when spent past `limit`; one instance per `InspectAsync` call).
- Produces: `sealed class CountingReadStream(Stream inner, long entryLimit, ByteBudget total) : Stream` counts bytes actually read and throws `PackageSourceException(UnsafeArchive, "entry exceeded its size cap")` past `entryLimit`, or `"archive exceeded total size cap"` when the shared `ByteBudget` (one per nupkg, `MaxTotalBytes`) runs out. Every entry the inspector opens is read through it. The total cap is the architect's call (plan review P7); the design's caps are unchanged.
- Produces (tests): `NupkgBuilder.Create(string id, string version).WithNuspec(...).WithFile(string path, byte[] content).WithRawEntryName(string name).Build() -> string tempPath`.
- `ContentHash` is the base64 SHA-512 of the nupkg bytes (matches the `.nupkg.sha512` file in the global packages folder).
- Type-reference fields are filled by Task 20; this task leaves them false.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task UmbracoPackageJson_UnderStaticWebAssets_Found()
[Fact] public async Task LegacyManifest_UnderContentAppPlugins_Found()   // plus App_Plugins/ and staticwebassets/ variants (S2 paths)
[Fact] public async Task DualMode_BothFlagsTrue()                       // Crumpled.RobotsTxt shape
[Fact] public async Task BasePath_ReadFromBuildProps()                  // build/ and buildTransitive/
[Fact] public async Task ClientAssets_JsUnderStaticWebAssets()
[Fact] public async Task LibTfms_Listed()
[Fact] public async Task ZipSlipEntry_IsUnsafeArchive()                 // "../evil" -> PackageSourceException Reason UnsafeArchive
[Fact] public async Task RootedEntry_IsUnsafeArchive()                  // "/etc/x", "C:\\x"
[Fact] public async Task EntryCountOverCap_IsUnsafeArchive()           // limits lowered to 10 in the test
[Fact] public async Task EntryOverSizeCap_IsSkippedWithEvidenceNote()
[Fact] public async Task NothingWrittenToDisk()                         // temp dir file count unchanged across InspectAsync
[Fact] public async Task ManifestEntriesOverOneMegabyte_AreNotParsed()  // only presence is recorded
[Fact] public async Task LyingEntrySize_StoppedByByteCount()            // central directory says 1 KB, entry inflates to 2 MB, MaxEntryBytes 1 MB -> UnsafeArchive
[Fact] public async Task TotalBytesOverCap_IsUnsafeArchive()            // 5 entries of 300 KB each, MaxTotalBytes 1 MB -> UnsafeArchive
[Fact] public async Task Props_WithDoctype_IsRejectedNoExpansion()     // props with <!DOCTYPE> and an external entity -> BasePath null, evidence note, no file or network access
[Fact] public void CountingReadStream_ExactlyAtLimit_Ok()              // limit 100, read 100 bytes -> no throw; 101 -> throws
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Inspect.Tests --filter "NupkgInspectorTests|ArchiveGuardTests|CountingReadStreamTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** with `ZipArchive` in `Read` mode over a `FileStream`. Path checks run on every entry name before any entry is opened. Entries are opened only through `CountingReadStream`; `ZipArchiveEntry.Length` is used only to skip entries declared over the cap. The props file is parsed with `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }`; the resulting `XmlException` becomes an evidence note, not a crash.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(inspect): archive guard and nupkg inspector`

### Task 20: Metadata type-reference scanner

**Files:**
- Create: `src/PackageCheck.Inspect/MetadataTypeScanner.cs`
- Create: `tests/Support/StubAssemblies/` (source in `tests/Support/StubAssemblies/src/`, compiled DLLs checked in with a `README.md` that says how to rebuild them)
- Test: `tests/PackageCheck.Inspect.Tests/MetadataTypeScannerTests.cs`

**Interfaces:**
- Produces: `record TypeRefScan(bool ReferencesManifestReader, bool ReferencesManagementApi)`; `static TypeRefScan MetadataTypeScanner.Scan(Stream peBytes)`; `NupkgInspector` calls it for each `lib/**/*.dll` under the entry cap and ORs the results.
- Matches: `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader` or `.PackageManifest` type references for the first flag; any type reference in a namespace starting `Umbraco.Cms.Api.Management` for the second.

Stub assemblies: three tiny class libraries compiled against stub types with those exact namespaces and names (no Umbraco package reference): one implementing `IPackageManifestReader`, one deriving from a stub `ManagementApiControllerBase`, one plain.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Stub_ManifestReader_Detected()
[Fact] public void Stub_ManagementApi_Detected()
[Fact] public void Plain_NothingDetected()
[Fact] public void NotAPeFile_ReturnsFalse_NoThrow()
[Fact] public async Task AssemblyNeverLoaded()
{
    await new NupkgInspector(ArchiveLimits.Default).InspectAsync(id, NupkgBuilder.WithDll(StubPath("ManifestReaderStub.dll")), default);
    // Name check, not a before/after diff: other tests in the run may load unrelated assemblies (plan review P14).
    Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "ManifestReaderStub");
}
[Fact] public void InspectAssembly_HasNoLoadApis() // reflection over PackageCheck.Inspect IL: no call to Assembly.Load*, AssemblyLoadContext.LoadFrom*, MetadataLoadContext
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Inspect.Tests --filter MetadataTypeScannerTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with `PEReader` and `MetadataReader.TypeReferences`. A `BadImageFormatException` returns both false. Add `Microsoft.CodeAnalysis.BannedApiAnalyzers` to `PackageCheck.Inspect` with `BannedSymbols.txt` listing `Assembly.Load*`, `Assembly.LoadFrom`, `Assembly.LoadFile`, `AssemblyLoadContext.LoadFrom*` and `System.Reflection.MetadataLoadContext`; with `TreatWarningsAsErrors` a banned call fails the build.
- [ ] **Step 3b: Prove the analyzer bites.** Add `_ = System.Reflection.Assembly.LoadFrom("x");` to `MetadataTypeScanner.cs`, run `dotnet build src/PackageCheck.Inspect`, expect error `RS0030`, then remove the line.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(inspect): metadata type-reference scan without loading`

### Task 21: Inspection heuristics H06, H07, H08, H09, H11 and wiring

**Files:**
- Create: `src/PackageCheck.Core/Heuristics/H06TfmFit.cs`, `H07UmbracoPackageJson.cs`, `H08LegacyManifestOnly.cs`, `H09ManifestReaderRef.cs`, `H11ManagementApiNoClient.cs`
- Modify: `src/PackageCheck.Core/Facts/FactsCollector.cs` (inspect own nupkg and Umbraco dependencies)
- Test: `tests/PackageCheck.Core.Tests/Heuristics/H06Tests.cs` ... `H11Tests.cs`, `FactsCollectorInspectionTests.cs`

**Interfaces:**
- Consumes: `INupkgInspector` (Task 19), `IPackageMetadataSource.OpenNupkgAsync` (Task 12).
- Produces: `PackageFacts.Own` and `PackageFacts.Dependencies` filled when `FactsOptions.Inspect`; dependencies inspected are those that themselves have an Umbraco path, at the depth S2 decided.
- Fills `PackageFacts.IsServerOnly` (defined in Task 14).
- Produces: `FactsCollector(IPackageMetadataSource meta, IMarketplaceSource? marketplace = null, INupkgInspector? inspector = null)`.

| Heuristic | Evidence |
|---|---|
| H06 | no lib TFM or dependency group compatible with the target TFM: Negative, Low; compatible: Info |
| H07 | `umbraco-package.json` in own or a dependency: Positive, High, detail names the package and `BasePath` |
| H08 | legacy manifest and no H07 and no H09: Negative, High, detail `legacy package.manifest only` |
| H09 | manifest-reader type reference in own or a dependency: Positive, High (or Medium per S3) |
| H11 (cuttable #2) | Management API refs, no JS and no manifest: Negative, Low, evidence only |

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void H08_DualMode_DoesNotFire()                 // Crumpled.RobotsTxt shape
[Fact] public void H07_InDependency_Counts()                  // Workflow + StaticAssets shape
[Fact] public void H09_InDependency_Counts()                  // SimpleDashboards + jcdcdev.Umbraco.Core shape
[Fact] public void H06_Net8LibOnTargetNet10_IsCompatible()    // MediaWiz shape
[Fact] public void InspectionHeuristics_SkippedForTargetBelow14()
[Fact] public async Task NoInspect_NoNupkgOpened()
[Fact] public async Task OverCap_FallsBackToMetadataOnly()    // evidence note "nupkg over size cap; metadata only", verdict from metadata rows
[Fact] public async Task UnsafeArchive_IsUnknownUnsafeArchive()
[Fact] public async Task TempNupkg_IsDeletedOnFailure()
[Fact] public async Task ServerOnly_RealPipeline_OkNotSuspicious()    // synthetic nupkg: lib/net10.0 dll only, bounded range allows 17.7.2; FactsCollector + VerdictEngine -> IsServerOnly true, Ok Medium
[Fact] public async Task DependencyWithClientAssets_NotServerOnly()  // own nupkg server-only, Umbraco dependency ships JS -> IsServerOnly false
[Fact] public async Task NoInspect_AstroboardShape_IsRow9()          // range ">= 13.8.1", dependency group net8.0, target 17.7.2/net10.0, Inspect false -> Ok Low with H03 evidence, no nupkg opened
[Fact] public async Task H08_ContentAppPluginsManifest_Fires()       // synthetic nupkg with content/App_Plugins/X/package.manifest only -> H08 Negative High, verdict Suspicious
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Core.Tests --filter "FullyQualifiedName~Heuristics|FactsCollectorInspectionTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** H11 is a separate class so cutting it is deleting one file and its registration.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(core): inspection heuristics H06-H09, H11`

### Task 22: H17 leftover `App_Plugins` in the project (cuttable #3)

**Files:**
- Create: `src/PackageCheck.Core/Heuristics/H17LeftoverAppPlugins.cs`, `src/PackageCheck.Core/Facts/ProjectFileSystem.cs`
- Test: `tests/PackageCheck.Core.Tests/Heuristics/H17Tests.cs`

**Interfaces:**
- Produces: `interface IProjectFileSystem { IReadOnlyList<string> AppPluginsFolders(string projectDir); }` (lists `App_Plugins/*` and `wwwroot/App_Plugins/*` folder names, read only). `PackageFacts.ProjectDirectories` feeds it.
- H17 fires only when a folder name matches the last segment of the package's `BasePath` (case-insensitive): Negative, Medium, evidence only (never changes the verdict).
- Produces: `FactsCollector(..., IProjectFileSystem? projectFs = null)` (appended last).

- [ ] **Step 1: Write the failing tests:**
  - `NameMatch_Warns`: `BasePath` `/App_Plugins/Contentment`, project has `App_Plugins/contentment` -> one H17 evidence, Negative, Medium, detail names the folder.
  - `NoNameMatch_Silent`: project has `App_Plugins/Other` -> no H17 evidence.
  - `NoBasePath_Silent`: `StaticWebAssetBasePath` null -> no H17 evidence.
  - `NeverChangesVerdict`: same facts with and without the folder -> identical `Verdict` and `Confidence`.
  - `WwwrootAppPlugins_AlsoChecked`: `wwwroot/App_Plugins/Contentment` -> one H17 evidence.
- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Core.Tests --filter H17Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(core): H17 leftover App_Plugins warning`

### Task 23: Cache and offline mode

**Files:**
- Create: `src/PackageCheck.Sources/Cache/FileCache.cs`, `CacheKey.cs`, `CachingMetadataSource.cs`, `CachingMarketplaceSource.cs`, `CachingInspector.cs`, `OfflineMetadataSource.cs`
- Create: `tests/Support/CacheHammer/` (console app used only by the two-process test; not packed)
- Test: `tests/PackageCheck.Sources.Tests/FileCacheTests.cs`, `OfflineTests.cs`

**Interfaces:**
- Produces: `FileCache(string root, TimeProvider clock)`; `bool TryRead<T>(CacheKey key, TimeSpan? maxAge, out T value)`; `void Write<T>(CacheKey key, T value)`; `void Clear()`; `CacheInfo Info()` (`record CacheInfo(string Root, int Entries, long Bytes)`). Default root `Environment.SpecialFolder.LocalApplicationData/umbraco-package-check`.
- Produces: `record CacheKey(string Kind, string Feed, string Id, string? Version, string? Extra)`; file name is the SHA-256 hex of the normalised key; `Feed` is always passed through `FeedSettings.Redact`.
- Produces: decorators `CachingMetadataSource(IPackageMetadataSource inner, FileCache c, TimeSpan maxAge)` (versions and details, 6 h default), `CachingMarketplaceSource` (24 h), `CachingInspector` (key id + version + contentHash + `InspectionCacheVersion`, no expiry).
- Produces: `OfflineMetadataSource` throws `PackageSourceException(Offline, ...)` for everything except `OpenNupkgAsync`, which serves the global packages folder only. `const int InspectionCacheVersion = 1` in `CachingInspector`; bump it whenever a heuristic's input changes.

Offline composition: `CachingMetadataSource(OfflineMetadataSource)` with infinite max age; Marketplace returns cache or null; overrides use the bundled snapshot. `--no-cache` skips both reads and writes, so a CI run leaves nothing behind. `--offline --no-cache` is a usage error (exit 2), because it could only ever produce `unknown`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Write_IsAtomic_TempThenRename()
[Fact] public void CorruptCacheEntry_IsAMiss()              // garbage bytes -> TryRead false, then Write replaces it
[Fact] public async Task ParallelRuns_ShareCacheSafely()    // two Tasks write/read the same 50 keys; no exception, all reads valid
[Fact] public async Task ParallelRuns_TwoProcesses_ShareCacheSafely() // starts tests/Support/CacheHammer (tiny console app over FileCache) twice on one root; both exit 0, every entry parses
[Fact] public void StaleTmpFiles_IgnoredAndCleaned()        // leftover "<name>.<guid>.tmp" files: TryRead never reads them, Info() does not count them, the next Write deletes .tmp files older than 1 h and keeps newer ones
[Fact] public void Expired_IsAMiss()                        // FakeTimeProvider + 6h
[Fact] public void CacheFiles_NeverContainCredentials()     // planted token in feed URL userinfo and NuGet.config; grep all cache files
[Fact] public async Task Offline_WarmCache_HttpHandlerNeverHit()   // FixtureHttpHandler.RequestCount == 0 across a full engine run
[Fact] public async Task Offline_ColdCache_IsUnknownOffline()
[Fact] public async Task InspectionCacheVersionBump_DiscardsOldEvidence()
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Sources.Tests --filter "FileCacheTests|OfflineTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Writes go to `<name>.<guid>.tmp` then `File.Move(..., overwrite: true)`. In offline mode the NuGet source is never constructed (belt and braces with the handler assertion).
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(sources): file cache and offline mode`

### Task 24: Golden fixture suite (M3 done)

**Files:**
- Create: `tests/PackageCheck.Golden.Tests/GoldenTests.cs`, `GoldenNupkgCache.cs`
- Create: `.github/workflows/golden.yml`

**Interfaces:**
- Consumes: `golden-packages.json` (Tasks 3, 7), the full engine from Tasks 12-23 with recorded HTTP.
- Produces: `GoldenNupkgCache.GetAsync(id, version, sha512)` downloads into `~/.cache/umbraco-package-check-tests/` (or `$GOLDEN_CACHE`) on first run, verifies the hash, and fails the test on mismatch. Real nupkgs are never committed.
- Produces (test helper): `Engine.Create(OverrideSet overrides)`; `Engine.Computed` is `Engine.Create(OverrideSet.Empty)`. Every golden test uses `Engine.Computed` except the one named for the seed override (Pascal, 2026-10-30).

- [ ] **Step 1: Write the failing tests** (all `[Trait("Category","Golden")]`, target 17.7.2 unless stated)

```csharp
[Fact] public async Task Astroboard_Suspicious_WithEvidence()   // computed verdict, no overrides
{
    var v = await Engine.Computed.EvaluateAsync(Pkg("astroboard", "1.1.3"), Target("17"), Inspect);
    Assert.Equal(Verdict.Suspicious, v.Verdict);
    Assert.Null(v.Override);
    Assert.Contains(v.Evidence, e => e.Detail == "open range >= 13.8.1");
    Assert.Contains(v.Evidence, e => e.Detail == "legacy package.manifest only");
}
[Fact] public async Task Astroboard_SeedOverride_EndToEnd()      // bundled seed loaded through BundledOverrideSource
{
    var seed = await new BundledOverrideSource().LoadAsync(default);
    var v = await Engine.Create(seed).EvaluateAsync(Pkg("astroboard", "1.1.3"), Target("17"), Inspect);
    Assert.Equal(Verdict.NoRelease, v.Verdict);
    Assert.Equal("override", v.Override!.Label);
    Assert.Equal(Verdict.Suspicious, v.Override.ComputedVerdict);
}
[Fact] public async Task SimpleDashboards_Ok_High_ViaH09()
[Fact] public async Task Workflow_Ok_ViaH07InStaticAssetsDependency()
[Fact] public async Task RobotsTxt_NotSuspiciousFromManifests()
[Fact] public async Task Contentment621_UpgradeTo633()
[Theory, MemberData(nameof(RecordedExpectations))] public async Task Recorded(string id, string version, string expected) // Task 7 Step 2 list
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Golden.Tests`. Expected: FAIL until the engine composition helper `Engine` exists; then implement it in the test project from production types only. (The real composition root arrives in Task 28a; this helper must not grow logic the CLI lacks.)
- [ ] **Step 3: Golden CI job.** `golden.yml`: on `push` to `main`, `pull_request` and weekly; `permissions: contents: read`; `actions/cache` (SHA-pinned) on the golden cache path; runs `dotnet test tests/PackageCheck.Golden.Tests`.
- [ ] **Step 4: Run** `dotnet test tests/PackageCheck.Golden.Tests` and then `dotnet test --filter "Category!=Golden"`. Expected: both `Failed: 0`. The M3 done-when (`suspicious` criteria pass; offline makes no HTTP) is now covered by `Astroboard_Suspicious_WithEvidence` and `Offline_WarmCache_HttpHandlerNeverHit`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add tests .github/workflows/golden.yml` / suggested message `test: golden fixture suite for real packages pinned by hash`

---

## M4: reports, exit codes, packaging

### Task 25: Report model, JSON writer and JSON Schema

**Files:**
- Create: `src/PackageCheck.Report/Model/ScanReport.cs` (and site, package, summary records), `ReportBuilder.cs`, `JsonReportWriter.cs`
- Create: `schema/report.schema.json` (embedded in `PackageCheck.Report`)
- Test: `tests/PackageCheck.Report.Tests/JsonReportWriterTests.cs`, `SchemaContractTests.cs`

**Interfaces:**
- Produces: `record ScanReport(int SchemaVersion, ToolInfo Tool, DateTimeOffset GeneratedAt, ReportOptions Options, IReadOnlyList<SiteReport> Sites, ReportSummary Summary, IReadOnlyList<string> Warnings)` (`Warnings` holds run-level warnings such as a skipped path); `record SiteReport(string Path, TargetReport Target, IReadOnlyList<ProjectReport> Projects, IReadOnlyList<PackageReport> Packages, IReadOnlyList<string> Warnings)`; `record TargetReport(string Requested, string Resolved, string Tfm, bool Prerelease)`; `record ReportSummary(IReadOnlyDictionary<string,int> Counts, bool Incomplete, string? Transitive)`; `PackageReport` has exactly the fields of design 7.6.
- Produces: `static ScanReport ReportBuilder.Build(IReadOnlyList<SiteResult> sites, IReadOnlyList<string> runWarnings, ReportOptions o, ToolInfo tool, TimeProvider clock)` where `record SiteResult(ScanInput Input, TargetVersion Target, IReadOnlyList<PackageVerdict> Verdicts, bool Incomplete)`.
- Produces: `static void JsonReportWriter.Write(ScanReport r, Stream s)` using System.Text.Json source generation, camelCase, enums as the kebab wire values in Global Constraints.

Schema additions beyond the design 7.6 example, all optional (so `schemaVersion` stays 1): top-level `warnings[]` (skipped paths, design 6.1), `sites[].warnings[]`, `summary.transitive` (`"unavailable"` per design 6.1), `sites[].target.prerelease`, `packages[].override.computedVerdict`, `.label`, `.stale` and `.replacement`. When an override applies, `packages[].verdict` is the override's verdict and `override.computedVerdict` is the table's. Record them in `docs/decisions.md`. `not-umbraco` packages are in the JSON always (`counts` excludes them); hiding is a console and md concern.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Json_EnumWireValues()               // "no-release", "conflicting-evidence", confidence null
[Fact] public void Json_ValidatesAgainstSchema()        // JsonSchema.Net evaluation over reports built from every golden/fixture scenario
[Fact] public void Json_UnknownCarriesReason()
[Fact] public void Json_FeedUrlsAndPathsHaveNoCredentials() // planted token absent
[Fact] public void Counts_ExcludeNotUmbraco()
[Fact] public void Schema_IsEmbedded()
[Fact] public void Json_Override_ShowsOverrideVerdict_AndComputed()  // verdict "no-release", override.label "override", override.computedVerdict "suspicious"
[Fact] public void Json_PrereleaseTarget_Marked()                     // target 18.0.0-rc2 -> sites[0].target.prerelease == true, resolved "18.0.0-rc2"
[Fact] public void Json_RunWarnings_Present()                         // warnings[0] == "no Umbraco project found in /x; skipped"
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Report.Tests --filter "JsonReportWriterTests|SchemaContractTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the model, builder, writer and schema (`$id` `https://raw.githubusercontent.com/PascalA94/Umbraco.Community.PackageCheck/main/schema/report.schema.json`).
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add schema src tests` / suggested message `feat(report): report model, JSON writer and schema v1`

### Task 26: Markdown report

**Files:**
- Create: `src/PackageCheck.Report/MarkdownReportWriter.cs`, `TextSafety.cs`
- Test: `tests/PackageCheck.Report.Tests/MarkdownReportWriterTests.cs` (+ Verify `.verified.md` snapshots)

**Interfaces:**
- Produces: `static string MarkdownReportWriter.Write(ScanReport r, bool verbose)`.
- Produces: `static string TextSafety.Markdown(string s)` (HTML-encode `& < > " '`, backslash-escape `` \ ` * _ { } [ ] ( ) # + - . ! | ~ ``, newlines to spaces, strip control chars); `static string TextSafety.Console(string s)` (strip C0/C1 control chars including ESC, then `Markup.Escape`). Task 27 uses the second.

Layout (design 7.6): one summary line; with more than one site, a site x package matrix first (cuttable #4); one table per verdict, worst first (`no-release`, `upgrade`/`replaced`, `suspicious`, `unknown`, `ok`); a `<details>` block per package with its evidence. Overrides show `override` or `local override`, the evidence link and `stale` when past `reviewBy`. Prereleases are mentioned on `no-release`. A footer links Umbraco17Check for code rules.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public Task SingleSite_Snapshot()   [Fact] public Task ThreeSites_HasMatrix_Snapshot()
[Fact] public void PackageIdWithMarkdownAndHtml_IsEscaped()   // id "<script>alert(1)</script>*x*" -> no raw "<script", no unescaped "*x*"
[Fact] public void OverrideSummaryInjection_IsEscaped()       // "](javascript:alert(1))" stays inert
[Fact] public void Override_IsLabelled_WithLink_AndStale()
[Fact] public void NeverUsesTheWordBroken()                   // over every snapshot output, case-insensitive
[Fact] public void NotUmbraco_HiddenUnlessVerbose()
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Report.Tests --filter MarkdownReportWriterTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Every package-controlled string goes through `TextSafety.Markdown`; links are built only from `https` URLs.
- [ ] **Step 4: Run again,** review and accept the snapshots. Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(report): markdown report with escaping and multi-site matrix`

### Task 27: Console report

**Files:**
- Create: `src/PackageCheck.Report/ConsoleReportWriter.cs`
- Test: `tests/PackageCheck.Report.Tests/ConsoleReportWriterTests.cs`

**Interfaces:**
- Produces: `static void ConsoleReportWriter.Write(ScanReport r, IAnsiConsole console, bool verbose)`; "top 3 actions" are the three worst packages by severity order, phrased "Move X to Y", "Replace X with Y", "Check X (suspicious): see evidence".

- [ ] **Step 1: Write the failing tests** (Spectre `TestConsole`)

```csharp
[Fact] public void NoColor_EnvVar_DisablesColour()
[Theory] [InlineData("id")] [InlineData("versionText")] [InlineData("evidenceDetail")] [InlineData("deprecationText")]
         [InlineData("marketplaceAlternate")] [InlineData("overrideSummary")] [InlineData("overrideReplacement")] [InlineData("siteWarning")] [InlineData("runWarning")]
public void Console_PackageText_IsInert(string source) // payload "pkg\u001b[31mred[/]\nnext" placed in that field -> output has no ESC byte, no markup exception, payload on one line
[Fact] public void TopThreeActions_WorstFirst()       // no-release, upgrade, suspicious, ok packages -> actions in that order, ok absent
[Fact] public void Verbose_PrintsEveryEvidenceLine()  // 5 evidence lines in -> 5 lines out
[Fact] public void Warnings_ArePrinted()              // one site warning and one run warning -> both appear
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Report.Tests --filter ConsoleReportWriterTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with Spectre tables. Every package-controlled string goes through `TextSafety.Console`.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(report): console report`

### Task 28a: `scan` command, options and orchestration (first working scan)

**Files:**
- Modify: `src/PackageCheck.Cli/Program.cs`, `CliApp.cs` (replace the placeholder)
- Create: `src/PackageCheck.Cli/Commands/ScanCommand.cs`, `ScanOrchestrator.cs`, `ServiceRegistration.cs`, `DurationParser.cs`, `PrivateIds.cs`, `PathSet.cs`
- Test: `tests/PackageCheck.Cli.Tests/ScanCommandTests.cs`, `ScanOrchestratorTests.cs`, `OptionTests.cs`

**Interfaces:**
- Produces: `static Task<int> CliApp.RunAsync(string[] args, CliEnvironment env)`; `record CliEnvironment(TextWriter Stdout, TextWriter Stderr, IAnsiConsole Console, HttpMessageHandler? Handler, TimeProvider Clock, string? CacheRoot)` so tests run in-process with fixtures.
- Produces: `record ScanRequest(IReadOnlyList<string> Paths, string Target, FailOn FailOn, bool Transitive, bool UseMsBuild, ScanOptions Options, bool Offline, string? OverridesPath, string? PrivateIdsPath, TimeSpan MaxAge, bool NoCache)`; `ScanOrchestrator.RunAsync(ScanRequest req, CancellationToken ct) -> Task<ScanReport>`; evaluates distinct (feed set, id, version, target) once, with `Parallel.ForEachAsync` and `MaxDegreeOfParallelism = 8`. `FailOn` is declared here as `enum FailOn { None, Suspicious, Upgrade, NoRelease }` and parsed in Task 28b; until then it is always `None`.
- Produces: `static IReadOnlyList<string> PathSet.Normalise(IEnumerable<string> paths)`: full path, trailing separator trimmed, duplicates removed (case-insensitive on case-insensitive file systems), first-seen order kept. A duplicate is scanned once, with no warning (architect's call, plan review P6).
- Produces: `PrivateIds.Load(string file)`: one id per line, `#` comments; listed ids become `unknown`, `private-feed` with no HTTP call.
- Exit codes in this task: 2 for any `UsageException` or unhandled exception (one line; stack trace only with `--verbose`), otherwise 0. Task 28b adds 1 and 3.

**Multi-path rule (design 6.1, Pascal 2026-10-30):** after `PathSet.Normalise`, any missing path throws `PathNotFoundException` before any scanning (exit 2, even if other paths are valid). Each remaining path is read; a `NoUmbracoProjectException` for one path adds the run warning `no Umbraco project found in <path>; skipped` and the path is dropped. If no path is left, rethrow (exit 2, message `no Umbraco project found`). A single path with no Umbraco project is the same rule with one path, so the single-path AC still holds.

Options are design 6.1 verbatim, plus `--private-ids <file>` (named in design 7.3 but missing from the 6.1 list). `-o` with `--format console` is a usage error. `--max-age` accepts `30m`, `6h`, `1d`. `--offline --no-cache` is a usage error (Task 23).

- [ ] **Step 1: Write the failing tests** (in-process, recorded HTTP, repo fixtures from Task 11)

```csharp
[Fact] public async Task NoUmbracoProject_Exit2_Message()              // stderr contains "no Umbraco project found"
[Fact] public async Task MissingPath_Exit2()                           // stderr "path not found: <path>"
[Fact] public async Task MultiPath_OneWithoutUmbraco_WarnsAndSkips()   // shape-01 + shape-10 -> exit 0, 1 site, warnings == ["no Umbraco project found in <shape-10>; skipped"]
[Fact] public async Task MultiPath_NoneWithUmbraco_Exit2()             // shape-10 + an empty temp folder -> exit 2, "no Umbraco project found"
[Fact] public async Task MultiPath_OneMissing_Exit2()                  // shape-01 + /does/not/exist -> exit 2, nothing scanned (RequestCount == 0)
[Fact] public async Task DuplicatePath_ScannedOnce()                   // "shape-01" and "shape-01/" -> 1 site, no warning
[Fact] public async Task NextMajorUnreleased_Exit2()
[Fact] public async Task UnhandledException_Exit2_OneLine()            // stderr is one line; with --verbose it includes "at "
[Fact] public async Task PrivateIds_SkippedWithoutHttp()               // listed id -> unknown/private-feed; no request URL contains that id
[Fact] public async Task CurrentAndNextMajor_ResolvePerSite()          // sites on 13 and 17 -> targets 13.x and 17.x for current; 14.x and 18.x for next-major
[Fact] public async Task OutputFile_WritesJson_AndConsoleSummaryStillPrinted()
[Fact] public async Task OutputWithConsoleFormat_Exit2()
[Theory] [InlineData("30m", 30)] [InlineData("6h", 360)] [InlineData("1d", 1440)] public void MaxAge_Parses(string text, int minutes)
[Theory] [InlineData("6x")] [InlineData("")] [InlineData("-1h")] public async Task MaxAge_Invalid_Exit2(string text)
[Fact] public async Task OfflineNoCache_Exit2()
[Fact] public async Task NoInspect_NoNupkgRequested()                  // no request URL ends ".nupkg"; no global-folder nupkg opened
[Fact] public async Task CacheDir_IsUsed()                             // entries appear under the given --cache-dir only
[Fact] public async Task SameIdVersionAcrossSites_FetchedOnce()        // two sites both on Contentment 6.2.1, same feeds -> registration request for contentment == 1
[Fact] public async Task Concurrency_NeverExceedsEight()               // 20 distinct packages, handler holds each request 50 ms -> 1 < MaxInFlight <= 8
[Fact] public async Task SiteWarnings_AppearInConsoleJsonAndMd()       // sln-missing-project fixture -> "Gone.csproj" warning in all three formats
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Cli.Tests --filter "ScanCommandTests|ScanOrchestratorTests|OptionTests"`. Expected: FAIL (placeholder still returns 2 for everything).
- [ ] **Step 3: Implement** with System.CommandLine 2.x and `ServiceCollection` wiring (online, offline and `--no-inspect` compositions chosen in `ServiceRegistration`). The composition loads overrides through `CompositeOverrideSource` (bundled plus `--overrides`). Delete `PlaceholderTests` from Task 1.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Smoke run.** Run: `dotnet run --project src/PackageCheck.Cli -- scan tests/Support/RepoFixtures/shape-01 --target 17.7.2; echo "exit=$?"`. Expected: a console table with a verdict per package and `exit=0`.
- [ ] **Step 6: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(cli): scan command, options and multi-path orchestration`

### Task 35a: Early real-site exercise (read-only, moved from Task 35)

Runs straight after the first working `scan`, so real-site surprises surface with a week of build time left rather than on release week (plan review P5). Task 35 repeats the comparison on the release candidate.

**Files:**
- Create: `docs/exercise.md` (findings only: counts, verdict surprises, timings; no client package lists beyond public package ids, no secrets). Each run is a dated section.

Every scanned repo is read-only. Before and after each run, `git -C <repo> status --porcelain` must print the same thing. Do not run `dotnet restore` or any build in those repos. Per Pascal's standing rule, scanning sends nothing anywhere except NuGet and Marketplace queries for public ids; private-feed ids come out `unknown`.

- [ ] **Step 1: Install a dev build** and prove `RollForward=Major`:

```bash
cd ~/Projects/Umbraco.Community.PackageCheck
dotnet pack src/PackageCheck.Cli -c Release -p:Version=0.1.0-dev.1 -o artifacts
dotnet tool install --tool-path /tmp/pc-dev --add-source artifacts Umbraco.Community.PackageCheck --version 0.1.0-dev.1
cd /tmp && DOTNET_ROOT=<sdk8 install> PATH=<sdk8 install>:$PATH /tmp/pc-dev/umbraco-package-check --version
```
Expected: `0.1.0-dev.1` both on SDK 10 and on SDK 8. The SDK 8 run starts from `/tmp` so the repo's `global.json` (SDK 10) does not apply (plan review P17). If SDK 8 is not installed, record it as not verified locally; the Task 30 matrix covers it.
- [ ] **Step 2: Single site, each target form**

```bash
/tmp/pc-dev/umbraco-package-check scan <sample> --target current
/tmp/pc-dev/umbraco-package-check scan <sample>/src/Sample.slnx --target 17.7.2 --format json -o /tmp/pc-sample.json; echo "exit=$?"
/tmp/pc-dev/umbraco-package-check scan <sample> --target next-major --prerelease --verbose
```
Expected: no crash; every package has a verdict; the JSON validates against `schema/report.schema.json`; `exit=0`; `git -C <sample> status --porcelain` unchanged.
- [ ] **Step 3: Multi-site matrix**

```bash
/tmp/pc-dev/umbraco-package-check scan <public sample site paths> --target 17.7.2 --format md -o /tmp/pc-audit.md
```
Expected: every sample site found (including one nested under a subfolder), a site x package matrix, any leftover `App_Plugins` H17 warning shown, timings recorded. Open `/tmp/pc-audit.md` in a markdown preview and check nothing renders as HTML.
- [ ] **Step 4: Unhappy paths.** A folder with no Umbraco project (any non-Umbraco repo): exit 2 and the message. `scan <sample> <non-Umbraco repo>`: the sample is scanned, the other path is skipped with the warning, exit 0. A path that doesn't exist: exit 2. `--target 99`: exit 2 with a clear message.
- [ ] **Step 5: Triage.** Each crash or wrong verdict is a bug: fix it with a test before Task 28b starts (use superpowers:systematic-debugging), or note it as a heuristic gap in `docs/exercise.md`. The CI-behaviour checks from the old Task 35 Step 4 run at the end of Task 28b, because they need exit codes 1 and 3. Remove `/tmp/pc-dev` afterwards.
- [ ] **Step 6: Checkpoint (Pascal commits):** `git add docs/exercise.md` plus any fixes and their tests / suggested message `test: early exercise against real sites`

### Task 28b: Exit codes and `--fail-on`

**Files:**
- Create: `src/PackageCheck.Cli/ExitCodes.cs`, `FailOnPolicy.cs`
- Modify: `src/PackageCheck.Cli/Commands/ScanCommand.cs` (parse `--fail-on`, return the computed code)
- Test: `tests/PackageCheck.Cli.Tests/ExitCodeTests.cs`, `FailOnPolicyTests.cs`

**Interfaces:**
- Consumes: `FailOn` (Task 28a), `ScanReport` (Task 25).
- Produces: `static FailOn FailOnPolicy.Parse(string text)` (exactly `none|suspicious|upgrade|no-release`; anything else throws `UsageException` listing the four values); `static bool FailOnPolicy.Exceeds(Verdict v, FailOn t)` (severity `no-release` > `upgrade` = `replaced` > `suspicious`; thresholds nest; `ok`, `unknown` and `not-umbraco` never exceed).
- Produces: `static int ExitCodes.Compute(ScanReport r, FailOn t)`: 3 if `t != None && r.Summary.Incomplete`; else 1 if any package exceeds; else 0.

Incomplete = any package `unknown` with reason `source-error`, or `summary.transitive == "unavailable"` with `--transitive` set. Per design 7.3, `not-found`, `private-feed`, `offline` and `unresolved-property` do not make a run incomplete; Marketplace and override fetch failures never do.

**Exit 2 versus 3 (plan review P12).** 2 means the request is wrong and no retry could fix it: a bad option, a missing path, no Umbraco site, or a target that does not exist on a feed that answered (`--target 99`, `next-major` with no release). It is decided before any report exists and wins over everything. 3 means a report was produced but a required source failed while `--fail-on` was set. If target resolution itself fails because a feed is unreachable (`--target 17` or `next-major` with nuget.org down), that is a source failure, not a usage error: the site's packages are `unknown`, `source-error`, the site warning says `target could not be resolved: source error`, `target.resolved` repeats `target.requested`, and the exit is 3 with `--fail-on` set or 0 with `summary.incomplete: true` without it. This applies the design 6.1 CI acceptance criteria to the case design 7.7's "target can't be resolved" row does not separate.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] [InlineData("none", FailOn.None)] [InlineData("suspicious", FailOn.Suspicious)] [InlineData("upgrade", FailOn.Upgrade)] [InlineData("no-release", FailOn.NoRelease)]
public void FailOn_Parse(string text, FailOn expected)
[Theory] [InlineData("broken")] [InlineData("Suspicious")] [InlineData("")] public async Task FailOn_Invalid_Exit2(string text) // stderr lists the four values
[Theory, MemberData(nameof(VerdictByThreshold))] public void Exceeds_Matrix(Verdict v, FailOn t, bool expected)
// 7 verdicts x 4 thresholds = 28 rows, generated from this table:
//   None: nothing. Suspicious: Suspicious, Upgrade, Replaced, NoRelease. Upgrade: Upgrade, Replaced, NoRelease. NoRelease: NoRelease.
//   Ok, Unknown and NotUmbraco are false for every threshold.
[Fact] public async Task FailOnSuspicious_OneSuspicious_Exit1()
[Fact] public async Task FailOnSuspicious_NuGetUnreachable_Exit3()
[Fact] public async Task FailOnNone_NuGetUnreachable_Exit0_Incomplete() // JSON summary.incomplete == true
[Fact] public async Task Both1And3_Returns3()
[Fact] public async Task Transitive_NoLock_WithFailOn_Exit3()
[Fact] public async Task NotFoundAndPrivateFeedOnly_NotIncomplete_Exit0()  // --fail-on suspicious
[Fact] public async Task MarketplaceDown_NeverExit3()                       // Marketplace 500s, NuGet fine, --fail-on suspicious -> 0 or 1 by verdicts
[Fact] public async Task LocalOverridesUrlDown_NeverExit3()
[Fact] public async Task TargetUnresolvable_SourceDown_FailOn_Exit3()       // --target 17, feed 5xx
[Fact] public async Task TargetUnresolvable_SourceDown_NoFailOn_Exit0_Incomplete()
[Fact] public async Task TargetDoesNotExist_Exit2()                         // --target 99, feed answers
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Cli.Tests --filter "ExitCodeTests|FailOnPolicyTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Real-site CI behaviours (moved from the old Task 35 Step 4).** With a fresh dev build installed as in Task 35a Step 1: `--fail-on suspicious` on the sample site (expect 0 or 1, matching the report); the same with networking blocked (`HTTPS_PROXY=http://127.0.0.1:9` and an empty `--cache-dir`) and expect `exit=3`; `--offline` after a warm run with the same proxy block, and expect the same verdicts as the warm run (any network attempt would turn packages `unknown`). Record results in `docs/exercise.md`. the sample site's `git status --porcelain` unchanged.
- [ ] **Step 6: Checkpoint (Pascal commits):** `git add src tests docs/exercise.md` / suggested message `feat(cli): exit codes and --fail-on thresholds`

### Task 28c: End-to-end security and architecture tests

**Files:**
- Test: `tests/PackageCheck.Cli.Tests/SecurityEndToEndTests.cs`, `ArchitectureTests.cs`

**Interfaces:**
- Consumes: `CliApp.RunAsync` and `CliEnvironment` (Task 28a). No production interfaces change unless a test finds a defect.

These pin behaviour built in earlier tasks across the whole pipeline. Each test is expected to pass on first run; one that fails is a defect to fix in the owning code (use superpowers:systematic-debugging), not by changing the test.

- [ ] **Step 1: Write the tests**

```csharp
[Fact] public async Task ScannedFolder_IsUnchanged()                   // hash every file under the fixture before and after a full scan with --transitive
[Fact] public async Task PlantedCredential_AbsentFromAllOutputs()      // NuGet.config with ClearTextPassword "PLANTED-TOKEN-123" and feed userinfo; grep console, stderr, json, md, cache dir
[Fact] public async Task MarkdownInjection_InCsprojId_EscapedInMd()     // fixture csproj references "<img src=x onerror=alert(1)>" -> md has no raw "<img"
[Fact] public async Task UnsafeArchive_EndToEnd_IsUnknown()            // global packages folder holds a zip-slip nupkg -> unknown/unsafe-archive, nothing written outside temp
[Fact] public void NoStaticMutableFields()                             // reflection over PackageCheck.* assemblies: static fields must be readonly or const (compiler-generated excluded)
[Fact] public void NoServiceLocator()                                  // no constructor in PackageCheck.* outside Cli.ServiceRegistration takes IServiceProvider
```

- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Cli.Tests --filter "SecurityEndToEndTests|ArchitectureTests"`. Expected: `Failed: 0`. If any fail, fix the defect, add a focused unit test in the owning project, and re-run.
- [ ] **Step 3: Checkpoint (Pascal commits):** `git add tests` (plus any fixes) / suggested message `test(cli): end-to-end security and architecture checks`

### Task 29: `explain` and `cache` commands

**Files:**
- Create: `src/PackageCheck.Cli/Commands/ExplainCommand.cs`, `CacheCommand.cs`
- Test: `tests/PackageCheck.Cli.Tests/ExplainCommandTests.cs`, `CacheCommandTests.cs`

**Interfaces:**
- `explain <packageId> [version] --target <v>`: version defaults to the latest stable; `--target` is required and must be a major, exact version or `latest-lts` (`current` and `next-major` need a site, so they are a usage error, exit 2). Prints every evidence line, the verdict and the upgrade choice. Accepts `--format`, `--offline`, `--prerelease`, `--no-inspect`, `--overrides`.
- `cache info` prints root, entry count and size; `cache clear` deletes only files inside the cache root.

`explain` is cut item 6 and `cache` is not: if cut, delete `ExplainCommand.cs` and its tests, keep `CacheCommand`, and note it in `CHANGELOG.md` as "planned".

- [ ] **Step 1: Write the failing tests:**
  - `Explain_Astroboard_PrintsEvidence`: `explain astroboard 1.1.3 --target 17.7.2` -> exit 0; output contains `open range >= 13.8.1`, `legacy package.manifest only`, and the override label with `computed: suspicious` (the bundled seed applies, as in `scan`).
  - `Explain_CurrentTarget_Exit2`: `--target current` -> exit 2, stderr names `current` and `next-major` as needing a site.
  - `Explain_DefaultsToLatestStable`: no version, fixture versions 6.2.1, 6.3.3, 7.2.1, 8.0.0-beta -> explains 7.2.1.
  - `CacheInfo_ShowsRoot`: prints the `--cache-dir` path, `entries: 3` and a byte count for a cache with 3 entries.
  - `CacheClear_EmptiesOnlyCacheRoot`: a sibling file next to the cache root survives; the root is empty afterwards.
- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Cli.Tests --filter "ExplainCommandTests|CacheCommandTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(cli): explain and cache commands`

### Task 30: Tool packaging and CI matrix

**Files:**
- Modify: `src/PackageCheck.Cli/PackageCheck.Cli.csproj` (README, icon, embedded schema and snapshot confirmed in the nupkg)
- Create: `assets/icon.png` (128x128, generic, no Umbraco logo), `.github/workflows/rerecord.yml`
- Modify: `.github/workflows/ci.yml` (packaging job)

- [ ] **Step 1: Packaging jobs** in `ci.yml` (plan review P17: the repo's `global.json` pins SDK 10, so SDK 8 and 9 cannot build inside the repo):
  - `pack`: SDK 10 only; packs the tool once with the CI version; uploads `artifacts/` and a copy of `tests/Support/RepoFixtures/shape-01` plus a pre-seeded cache dir as a workflow artifact.
  - `install-matrix`: needs `pack`; matrix SDK `8.0.x`, `9.0.x`, `10.0.x` (only that SDK installed per leg); no checkout; downloads the artifact into `$RUNNER_TEMP/pc` and runs everything from there: `dotnet tool install --tool-path ./t --add-source ./artifacts Umbraco.Community.PackageCheck --version <ci version>`, `./t/umbraco-package-check --version`, then `./t/umbraco-package-check scan ./shape-01 --offline --cache-dir ./cache --target 17.7.2`, asserting exit 0.
- [ ] **Step 2: Re-record job (cut item 5).** `rerecord.yml`: weekly and manual; runs the recorded-HTTP tests with `PACKAGECHECK_RECORD=1` and uploads the diff of `tests/Support/Http` as an artifact (no push, `permissions: contents: read`). If cut, Task 36 Step 1 re-records by hand.
- [ ] **Step 3: Verify locally**

```bash
cd ~/Projects/Umbraco.Community.PackageCheck
dotnet pack src/PackageCheck.Cli -c Release -p:Version=0.1.0-local -o artifacts
unzip -l artifacts/Umbraco.Community.PackageCheck.0.1.0-local.nupkg | grep -E "report.schema.json|README.md|icon.png|PackageCheck.Core.dll"
dotnet tool install --tool-path /tmp/pc-local --add-source artifacts Umbraco.Community.PackageCheck --version 0.1.0-local
/tmp/pc-local/umbraco-package-check --version
```
Expected: the `unzip` grep lists `README.md`, `icon.png` and `PackageCheck.Core.dll`, plus `report.schema.json` as a content file under `tools/`; the embedded copies inside the DLLs are covered by `Schema_IsEmbedded`. `--version` prints `0.1.0-local`. Remove `/tmp/pc-local`.
- [ ] **Step 4: Run the full suite.** Run: `dotnet test --filter "Category!=Golden"`. Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src assets .github` / suggested message `build: tool packaging and SDK 8/9/10 install matrix`

### Task 30b: Release workflow and `0.1.0-rc.1` rehearsal (M4 done, by Fri 4 Dec)

Moved forward from Task 33 so `release.yml` and NuGet publishing are first used on a release candidate, not on release day (plan review P5).

**Files:**
- Create: `.github/workflows/release.yml`, `CHANGELOG.md` (an `0.1.0-rc.1` entry)

- [ ] **Step 1: Release workflow.** `release.yml` on `v*` tags: build, test (non-golden), pack with the tag version, push to NuGet using trusted publishing if the account supports it, else a repository secret holding the key scoped to `Umbraco.Community.PackageCheck*`. Create a GitHub release, marked prerelease when the tag has a `-` suffix. `permissions: contents: write` (GitHub release) and `id-token: write` only if trusted publishing is used. Actions pinned by SHA. No `pull_request_target`.
- [ ] **Step 2: Lint the workflow.** Run: `actionlint .github/workflows/*.yml` (install with `brew install actionlint` if missing). Expected: no output, exit 0.
- [ ] **Step 3: Checkpoint (Pascal commits):** `git add .github/workflows/release.yml CHANGELOG.md` / suggested message `build: release workflow`
- [ ] **Step 4: Pascal sets up publishing** (the trusted-publishing policy on nuget.org, or the scoped-key secret), then tags `v0.1.0-rc.1` and pushes the tag. Claude never handles the key.
- [ ] **Step 5: Verify the rehearsal.** The `release.yml` run is green; `0.1.0-rc.1` shows on nuget.org as a prerelease; the GitHub release is marked prerelease. Then, from a temp folder with only SDK 8 on `PATH`: `dotnet tool install --tool-path /tmp/pc-rc Umbraco.Community.PackageCheck --version 0.1.0-rc.1 && /tmp/pc-rc/umbraco-package-check --version`. Expected: `0.1.0-rc.1`. Any fault found here is fixed and re-rehearsed as `rc.2`; never fixed for the first time on 0.1.0.

**M4 done-when:** `dotnet tool install` works from nuget.org on SDK 8; the schema file is in the package; `0.1.0-rc.1` was published by `release.yml`.

---

## M5: overrides repo, README, release

### Task 31: The `umbraco-package-compat` data repo

**Files (in `~/Projects/umbraco-package-compat`, a new public repo Pascal creates as `PascalA94/umbraco-package-compat`):**
- Create: `overrides/astroboard.yml`, `schema/override.schema.json`, `umbraco-versions.json`, `tools/compile.py` (or a small .NET script; builder's choice per design 12), `.github/workflows/build.yml`, `LICENSE` (CC0 1.0), `CONTRIBUTING.md`, `README.md`

- [ ] **Step 1: Pascal creates the public repo** and clones it to `~/Projects/umbraco-package-compat`. Check `git config user.email` prints the no-reply address; the `includeIf` only covers `~/Projects/Umbraco.Community.*`, so if it prints anything else, Pascal sets it for this repo himself.
- [ ] **Step 2: Write the failing check:** `build.yml` validates every `overrides/*.yml` against the schema on `pull_request` (no `pull_request_target`, `permissions: contents: read`), and on a `v*` tag compiles `overrides.json` (all files plus `umbraco-versions.json` embedded) and `overrides.json.sha256`, and attaches both to the release (`permissions: contents: write` on that job only). Add a deliberately invalid `overrides/bad.yml` and run `python3 tools/compile.py --check`. Expected: non-zero exit naming `bad.yml`.
- [ ] **Step 3: Implement** `tools/compile.py --check` and `--out dist/`; move the seed entries from the tool's `data/overrides.seed.json` into per-package YAML; delete `bad.yml`. Run `python3 tools/compile.py --check && python3 tools/compile.py --out dist && shasum -a 256 dist/overrides.json`. Expected: exit 0 and a hash matching `dist/overrides.json.sha256`.
- [ ] **Step 4: Governance.** `CONTRIBUTING.md` holds design 7.8's governance rules verbatim. Branch protection on `main` requiring one approving review is Pascal's setting; the two-maintainer rule starts once a co-maintainer joins (D5), which `README.md` says openly.
- [ ] **Step 5: Checkpoint (Pascal commits, in the compat repo):** `git add .` / suggested message `feat: override schema, seed entries and release build`. Pascal tags `v2026.12.0` to produce the first release asset.

### Task 32: Fetch the community overrides release

**Files:**
- Create: `src/PackageCheck.Sources/Overrides/CommunityOverrideSource.cs`, `tools/refresh-snapshot.sh`
- Test: `tests/PackageCheck.Sources.Tests/CommunityOverrideSourceTests.cs`

**Interfaces:**
- Produces: `CommunityOverrideSource(HttpClient http, FileCache cache, Uri latestReleaseApi)` implementing `IOverrideSource`; fetches the latest release's `overrides.json` and `.sha256`, verifies the hash, caches 24 h; on any failure or hash mismatch, falls back to cache, then to the bundled snapshot, with a warning. Never causes exit 3. Also supplies the `umbraco-versions.json` table, replacing the embedded one when newer.
- `tools/refresh-snapshot.sh` downloads the latest asset, checks the hash, and writes `data/overrides.seed.json` for the next tool release.

This task is cut item 7. If cut, the composition keeps `BundledOverrideSource` plus `LocalOverrideSource`, README says the community file arrives with each tool release, and `tools/refresh-snapshot.sh` still runs before release.

- [ ] **Step 1: Write the failing tests:**
  - `HashMismatch_FallsBackToSnapshot_WithWarning`: `.sha256` does not match -> entries equal the bundled snapshot's; one warning containing `hash mismatch`.
  - `NetworkError_FallsBackToCache`: 24 h-old cached set present, fetch 500s -> cached entries returned; one warning.
  - `Offline_UsesSnapshotOnly_NoHttp`: `RequestCount == 0`; entries equal the snapshot's.
  - `LocalOverrides_StillWin`: same id and range in community and local -> applied entry `Origin == Local`, label `local override`.
  - `CommunityFailure_NeverIncomplete`: fetch fails, `--fail-on suspicious`, packages otherwise `ok` -> exit 0, `summary.incomplete == false`.
  - `NewerVersionsTable_ReplacesEmbedded`: asset carries a table with major 19 -> `UmbracoVersionsTable` includes 19.
- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Sources.Tests --filter CommunityOverrideSourceTests`. Expected: FAIL.
- [ ] **Step 3: Implement**, and run `tools/refresh-snapshot.sh` once against the real `v2026.12.0` asset.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests tools data` / suggested message `feat(sources): community overrides release with hash check and snapshot fallback`

### Task 33: README and Marketplace metadata

**Files:**
- Modify: `README.md`, `CHANGELOG.md`, `src/PackageCheck.Cli/PackageCheck.Cli.csproj` (tags per S5)
- Create: `umbraco-marketplace.json` (fields per S5), `docs/heuristics.md`

The release workflow moved to Task 30b.

- [ ] **Step 1: README** covers: what it does; install (`dotnet tool install -g Umbraco.Community.PackageCheck` and local tool manifest); the five use cases from design 3 as commands; verdicts and confidence tables; exit codes table (design 7.7) with the 2-versus-3 rule from Task 28b; `--fail-on` severity order; several paths and the skip warning; prerelease targets and the release-base rule; offline and cache; private feeds, credentials per S1b, and `--private-ids`; overrides, the override-versus-computed verdict, and how to contribute (link to the compat repo); the JSON schema URL; "suspicious, never broken" wording; credit and link to Umbraco17Check for code rules; positioning per S6. No AI attribution.
- [ ] **Step 2: `docs/heuristics.md`** lists H01-H19 with 0.1 status, weights and the truth table as built.
- [ ] **Step 3: Verify the docs build into the package.** Run: `dotnet pack src/PackageCheck.Cli -c Release -p:Version=0.1.0-docs.1 -o artifacts && unzip -p artifacts/Umbraco.Community.PackageCheck.0.1.0-docs.1.nupkg Umbraco.Community.PackageCheck.nuspec | grep -E "tags|readme|license"`. Expected: the S5 tag set, `README.md`, `MIT`. Also `grep -ri "broken" README.md docs/heuristics.md umbraco-marketplace.json` prints only the override status name `broken` in the overrides section.
- [ ] **Step 4: Checkpoint (Pascal commits):** `git add README.md CHANGELOG.md umbraco-marketplace.json docs src` / suggested message `docs: README, heuristics reference and Marketplace metadata`

### Task 34: `--msbuild` exact versions (cuttable #1)

**Files:**
- Create: `src/PackageCheck.Inputs/MsBuildListRunner.cs`
- Test: `tests/PackageCheck.Inputs.Tests/MsBuildListRunnerTests.cs`

**Interfaces:**
- Produces: `MsBuildListRunner(IProcessRunner runner)`, `Task<IReadOnlyList<PackageRef>> ListAsync(string solutionOrProject, bool includeTransitive, CancellationToken ct)` running `dotnet list <path> package --format json [--include-transitive]` with no shell, arguments as an array, and a 5-minute timeout. Requires a prior restore; if the command fails, the site warning says "run dotnet restore first" and the run falls back to file reading with `TransitiveStatus.Unavailable` when `--transitive` was asked for.

- [ ] **Step 1: Write the failing tests** with a fake `IProcessRunner` returning canned JSON:
  - `ParsesTopLevelAndTransitive`: canned output with Contentment 6.2.1 top-level and Umbraco.Workflow.Core transitive -> 2 refs, `Direct` true and false.
  - `CommandFails_FallsBackWithWarning`: exit code 1 -> warning `run dotnet restore first`, file-reading refs returned, `TransitiveStatus.Unavailable` when transitive was asked.
  - `PathWithSpaces_PassedAsSingleArgument`: `/x/My Site/Site.sln` -> the runner receives it as one argument.
  - `Timeout_FallsBackWithWarning`: runner exceeds 5 minutes (fake clock) -> same fallback.
- [ ] **Step 2: Run** `dotnet test tests/PackageCheck.Inputs.Tests --filter MsBuildListRunnerTests`. Expected: FAIL.
- [ ] **Step 3: Implement**, and wire `InputOptions.UseMsBuild`.
- [ ] **Step 4: Run again.** Expected: `Failed: 0`.
- [ ] **Step 5: Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat(inputs): --msbuild exact versions via dotnet list package`

If this task is cut, remove the `--msbuild` option, note it in `CHANGELOG.md` as "planned", and record the cut in `docs/decisions.md`.

### Task 35: Exercise the release candidate against real sites

Steps 1-5 of the reviewed plan's Task 35 moved to Task 35a (and the CI checks to Task 28b Step 5). This task re-runs the comparison on the published release candidate.

**Files:**
- Modify: `docs/exercise.md` (a new dated section; same content rules as Task 35a)

The read-only rules of Task 35a apply: `git -C <repo> status --porcelain` unchanged before and after, no restore or build in scanned repos.

- [ ] **Step 1: Install the latest rc from nuget.org** (`0.1.0-rc.1`, or the latest `rc.N` from Task 30b) into `/tmp/pc-rc` from a temp folder, and re-run Task 35a Steps 2 and 3 with it. Expected: same as Task 35a, and no verdict differs from the last `docs/exercise.md` run without a recorded reason.
- [ ] **Step 2: Compare** the the sample site verdicts against what the October v17 upgrade actually chose (Contentment 6.3.3, GMaps 17.3.1, Skybrud 17.0.5, BlockPreview 5.6.0, Workflow and Engage held back; design section 9). Each disagreement is a bug to fix with a test (use superpowers:systematic-debugging) or a heuristic gap to note in `docs/exercise.md` for the M6 backtest.
- [ ] **Step 3: Record what could not be verified** (for example the SDK 9 leg locally, if SDK 9 is not installed: the CI matrix covers it).
- [ ] **Step 4: Checkpoint (Pascal commits):** `git add docs/exercise.md` plus any fixes and their tests / suggested message `test: exercise 0.1 RC against real sites`. A fix here needs a new `rc.N` through Task 30b Steps 4-5 before Task 36.

### Task 36: Release 0.1.0 (target Wed 9 Dec 2026)

`release.yml` has already shipped at least one rc (Task 30b), so this is a repeat of a rehearsed path. If the 9 Dec date slips, the buffer runs to Mon 14 Dec; apply the cut rule rather than move the deadline.

- [ ] **Step 1: Pre-flight.** Run: `dotnet test` (all, including golden) and confirm CI is green on `main`. Expected: `Failed: 0`. Refresh the overrides snapshot (`tools/refresh-snapshot.sh`, or by hand if Task 32 was cut) and set `CHANGELOG.md` to `0.1.0`. If `rerecord.yml` was cut, re-record the HTTP fixtures by hand first (`PACKAGECHECK_RECORD=1 dotnet test --filter "NuGetMetadataSourceTests|MarketplaceClientTests"`) and review the diff.
- [ ] **Step 2: Make the repo public** (Pascal), so the schema URL and README links resolve. Check `curl -sI https://raw.githubusercontent.com/PascalA94/Umbraco.Community.PackageCheck/main/schema/report.schema.json | head -1` prints `HTTP/2 200`.
- [ ] **Step 3: Checkpoint (Pascal commits):** `git add CHANGELOG.md data` / suggested message `chore: release 0.1.0`. Pascal tags `v0.1.0` and pushes; `release.yml` publishes.
- [ ] **Step 4: Verify on a clean machine or container** with only SDK 8: `dotnet tool install -g Umbraco.Community.PackageCheck --version 0.1.0 && umbraco-package-check --version`. Expected: `0.1.0`.
- [ ] **Step 5: Marketplace.** Within 24 h, the package appears on the Umbraco Marketplace as a Tool (or the S5 fallback outcome). Pascal posts in forum thread 7933 announcing 0.1 ("own it, announce it"); the overrides pitch waits for the M6 backtest (D5).

---

## Later (separate plans)

- **M6 backtest** on the 10 sites' pre-upgrade commits; precision and recall in the README (decides whether CI may ever block, D4).
- **M7 GitHub Action** (`umbraco-package-check-action`, composite, SARIF via a new `--format sarif`) and the mf-harness advisory job (a PR to mf-harness, after M6; blocking needs an ADR and `security-analyst` sign-off).
- **M8 H10** removed-API index (stretch).
- **M9 phase 3** backoffice "Package health" view, `Umbraco.Community.PackageCheck.Backoffice`, after spike S7.
- Overrides repo pitch, co-maintainer recruitment and offer to the Community Package Team.

---

## Plan review changes (2026-10-30)

Source: `plan-review.md` (qa-reviewer, 19 findings). Pascal's decisions are final for P1, P2, P5, P6 and P15; every other finding is applied as the reviewer proposed. Task numbers are kept so the review's references still resolve; new tasks have letter suffixes. Task count: 36 before, 41 now (added 2b, 28a-c replacing 28, 30b, 35a).

| ID | What changed |
|---|---|
| P1 | Golden and engine tests run with `OverrideSet.Empty` and prove the computed verdict (Global Constraints, Tasks 7, 17, 24). New `Astroboard_SeedOverride_EndToEnd` (Task 24): `no-release`, label `override`, `computedVerdict` `suspicious`. JSON test for override versus computed verdict (Task 25). Design 6.1 AC, section 8 and section 9 reworded. |
| P2 | `TargetVersion.RangeVersion` (release base) used by every range check (Global Constraints, Tasks 13, 15, 17, 18). Tests: `PrereleaseTarget_RangeVersionIsReleaseBase`, `H01_PrereleaseTarget_ComparesReleaseBase`, `H02_PrereleaseTarget_UpperBoundAtBase_Excludes`, `PrereleaseTarget_UsesReleaseBase_EndToEnd`, `Json_PrereleaseTarget_Marked`. Design 6.1 rule added. |
| P3 | Task 12: multi-feed outcome table and `MixedFeeds_Matrix` theory (404/401, 404/200, 401/200, 5xx/200, refused/200, 5xx/404, 404/404), plus 403, connection refused, nearest `NuGet.config` and source-mapping tests. |
| P4 | New Task 2b, spike S1b: real private feed through the credential provider with `nonInteractive: true`, no prompt and under 30 s; fallback to `NuGet.config` credentials only. Task 12 and README (Task 33) follow its decision. |
| P5 | New Task 30b moves `release.yml` out of Task 33 and rehearses it with `0.1.0-rc.1` by Fri 4 Dec. Old Task 35 Steps 1-5 become Task 35a, straight after the first working `scan` (Task 28a); the CI-behaviour step runs at the end of Task 28b because it needs exit codes. Task 35 keeps the comparison and re-runs on the rc. Release target Wed 9 Dec (Task 36). |
| P6 | Multi-path rule in Global Constraints and Task 28a (`PathSet`, warn-and-skip, exit 2 only when no site, missing path exit 2, duplicates scanned once with no warning). Top-level `warnings[]` in the report (Task 25). Design 6.1 path handling and AC updated. |
| P7 | `CountingReadStream` and `ByteBudget` enforce caps on bytes actually read; new 500 MB total cap, marked architect's call (Global Constraints, Task 19). Tests for a lying entry size, the total cap, a DOCTYPE in the props, and the exact limit. Design caps unchanged; the total cap is plan-level only. |
| P8 | Task 21: `ServerOnly_RealPipeline_OkNotSuspicious`, `DependencyWithClientAssets_NotServerOnly`, `NoInspect_AstroboardShape_IsRow9`, `H08_ContentAppPluginsManifest_Fires`. Astroboard `--no-inspect` added to the Task 7 recorded goldens. |
| P9 | "Interface order" note under File structure. Exceptions move to Task 8 (`UsageException`, `NoUmbracoProjectException`, `PathNotFoundException`; `NoProjectFoundException` removed). `InspectionResult` and `MarketplaceInfo` records created in Task 14; override model records in Task 17. `FactsCollector` gains optional constructor parameters in Tasks 16, 21 and 22. `NupkgTooLargeException` declared in Task 12. |
| P10 | `ScanInput.SiteUmbracoVersion` is nullable with `SiteUmbracoVersion_NullWhenNoUmbracoRef` (Task 11). `OverrideApplier.Apply` takes `bool anotherVersionClears` (Task 18). `FailOn_Parse` and `FailOn_Invalid_Exit2` (Task 28b). The `NupkgHandle` deviation from design 7.2 is logged in `docs/decisions.md` (Task 12). |
| P11 | Task 18: tests for `works`, `partial`, `broken` to `upgrade`, non-matching `packageVersions`, `replacement`, and the `--overrides` URL form (https only; http is exit 2; fetch failure is a warning). Overrides apply to the current version; `broken`/`partial` entries block matching upgrade candidates (architect's call, `BlocksCandidate`). |
| P12 | Task 28b: 28-row `Exceeds_Matrix` (7 verdicts x 4 thresholds), option tests in Task 28a, and a written exit 2 versus 3 rule. **Note:** a target that cannot be resolved because a feed is down is treated as a source failure (exit 3 with `--fail-on`, else 0 and incomplete), not design 7.7's exit 2. This follows design 7.3 and the 6.1 CI acceptance criteria, which 7.7 does not separate; design.md is not edited for it. |
| P13 | One-line Facts in Tasks 8-10: malformed `.sln`/`.slnx`, duplicate solution entries, open and exact ranges, bare versions, malformed `Directory.Packages.props`, CPM disabled, `GlobalPackageReference`, duplicate `PackageReference`, malformed lock file. Same version across projects in Task 11. Malformed input is a warning and a skip, never a crash. |
| P14 | `AssemblyNeverLoaded` checks the stub's assembly name instead of diffing the AppDomain, plus `BannedApiAnalyzers` with a step proving it fails the build (Task 20). S3's no-load check uses names too (Task 4). `HttpPolicy` takes a `TimeProvider`; retry tests use `FakeTimeProvider` (Task 12). |
| P15 | S3 control set fixed to 12 named ids with an objective "working" definition and a false-positive definition (Task 4). Ids are as recalled, with two named reserves. S6 is informational with no pass criterion (Task 6). Results review with Pascal booked for Fri 6 Nov (Task 7 Step 3, calendar). |
| P16 | Cut list extended with `rerecord.yml`, `explain` and the live override fetch, after the design's four (calendar). Cutting `explain` or the live fetch drops something the design lists, so each needs Pascal's agreement first. Task 28 split into 28a (scan and orchestration), 28b (exit codes), 28c (end-to-end security and architecture). Cut checks on Mon 30 Nov and Fri 4 Dec. |
| P17 | Task 30: pack once on SDK 10; the SDK 8/9/10 legs install and run from `$RUNNER_TEMP` with no checkout, so `global.json` does not apply. Same approach for the local SDK 8 check (Task 35a). |
| P18 | Review Focus widened: every console text source (theory in Task 27), same version in two projects (Task 11), warnings reach every output (Tasks 27, 28a), two-process cache test and stale `.tmp` cleanup (Task 23). |
| P19 | H05 list named (`Umbraco.Cms.Web.BackOffice`, extended only with a cited source) and pinned by a test (Task 15). Expected values added to test lists in Tasks 22, 27, 29, 32 and 34. Fetch-once and max-8 concurrency tests (Task 28a), using `FixtureHttpHandler.MaxInFlight` (Task 12). |

**Revised calendar:** M0 Thu 5 Nov, review with Pascal Fri 6 Nov; M1 Fri 13 Nov; M2 Sun 22 Nov; M3 Sun 29 Nov (cut check Mon 30 Nov); M4 with `0.1.0-rc.1` Fri 4 Dec (cut check); 0.1.0 Wed 9 Dec; deadline Mon 14 Dec.

**Self-review (writing-plans, re-run 2026-10-30):**
- Spec coverage: the three design changes (astroboard AC, prerelease release base, several paths) each have a task and named tests (Tasks 24/25, 13/15/17/25, 28a/25). No design 6.1 acceptance criterion is left without a test.
- Type consistency: checked `TargetVersion.RangeVersion`, `OverrideApplier.Apply`/`BlocksCandidate`, `FactsCollector`'s constructor growth, `ReportBuilder.Build` (now takes run warnings), `ScanReport.Warnings`, `HttpPolicy`, `FixtureHttpHandler.MaxInFlight`, `ArchiveLimits.Default`, `ByteBudget`, `BundledOverrideSource`, `FailOn` (declared 28a, parsed 28b) and the exception names. Every type is now created in or before the first task that compiles against it.
- Proportion: the plan grew from about 1,160 to about 1,500 lines against an 810-line spec. Nearly all of the growth is test names and assertions the review asked for; code bodies are still limited to the upgrade walk and short test examples.

## Portfolio decisions applied after the plan review (2026-10-30)

These supersede anything above that conflicts with them.

- **Repo visibility:** the repo goes **public at Task 1** (scaffold), not at Task 36. GitHub is on the Free plan, so CodeQL, rulesets and gitleaks are available from day one. Task 36 keeps only the release checks.
- **Public docs:** only `design.md` and `plan.md` are copied into the repo's `docs/`. `plan-review.md` stays in the private workspace.
- **PR checks:** run in the repo's own CI (gitleaks, osv-scanner, `dotnet list package --vulnerable`), with no org enrolment.
