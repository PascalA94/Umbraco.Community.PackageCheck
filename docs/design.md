# Design: Umbraco.Community.PackageCheck

| | |
|---|---|
| Package | 09 Package compatibility checker |
| NuGet ID | `Umbraco.Community.PackageCheck` (dotnet tool) |
| Command | `umbraco-package-check` |
| Owner | Pascal Meyer |
| Status | Design, ready to plan. Nothing built |
| Date | 2026-10-29 |
| Source | [`../09-package-check/scope.md`](../09-package-check/scope.md) (research, deep scope, Decisions) |
| Deadline | Release 0.1 before **14 Dec 2026** (v13 end of life) |

**Tags used in this doc:**
- **[verified]**: fetched, downloaded or read during the research session on 2026-10-29.
- **[unverified]**: recalled or inferred. Check before relying on it.
- **(architect's call)**: decided in this doc, not by Pascal. The builder may change it with a reason.
- Everything reflects 2026-10-29. Umbraco, NuGet and the Marketplace move fast, so re-check in M0.

---

## 1. Intent and why

**Intent:** tell a developer, before they upgrade, which of their Umbraco packages will really work on the target version, and what to move each one to.

**Why it matters:**
- Marketplace "compatible" badges come from NuGet dependency ranges. A loose range (`>= 13.8.1`) gives a false badge [verified].
  - Forum thread 7933 lists about 17 packages badged U17 that don't work [verified].
  - Example: `astroboard 1.1.3` is badged v17 but ships only a legacy AngularJS `package.manifest` [verified].
  - Of 517 v17-badged packages, 12 have no net9/net10 build and an open range from 16 or below [verified].
- Manifesto has just done a v17 upgrade across 10 sites by hand. Picking each package version took a manual flat-container query per package.
- v13 hits end of life on 14 Dec 2026. Upgrade demand peaks from now to Q1 2027.
- Nothing else does this:
  - **Umbraco17Check 0.1.0** is the closest rival. It's hard-coded to v17, never opens a nupkg, checks direct refs only, ignores `NuGet.config` and always exits 0 [verified from source].
  - No HQ upgrade-readiness tool exists. Core's upgrade checker is obsolete and only ever checked CMS versions [verified, 17.7.2 source].
  - No compatibility checker is listed on the Marketplace [verified].

**Positioning:** the version-agnostic, content-inspecting, CI-ready **package** checker. Credit and link Umbraco17Check for code rules.

---

## 2. Goals, non-goals and anti-goals

### Goals
- G1. Per package, give a verdict for any target Umbraco version: `ok`, `upgrade` (to X), `no-release`, `suspicious`, `replaced`, `unknown`.
- G2. Look **inside** the nupkg, not just the declared range. This is the differentiator.
- G3. Every verdict shows its evidence and a confidence level that comes from the evidence.
- G4. Work on real agency repos from day one: `.sln`, `.slnx`, CPM, `VersionOverride`, `packages.lock.json` v2, private feeds.
- G5. Usable in CI: stable JSON schema, meaningful exit codes, fails closed when a source is down and `--fail-on` is set.
- G6. Pick the right patch inside a major (`--target 17.7.2` gives "Contentment 6.3.3, not 7.x").
- G7. Audit many sites in one run and produce a site x package matrix.
- G8. Ship 0.1 before 14 Dec 2026.

### Non-goals
- **Code rules.** No scanning of the site's own C#, Razor, uSync or AngularJS code (Decision 1). Link to Umbraco17Check instead.
- Upgrading anything. The tool reports; it never edits a csproj or props file.
- CMS version upgrade advice (migrations, breaking-change guides).
- Vulnerability scanning. BlendInteractive SecurityDashboard and `dotnet list package --vulnerable` already do this.
- Running or booting packages. The "smoke lab" is a later, optional idea.
- H10 removed-API index in 0.1. It's a stretch goal (M8).

### Anti-goals (it fails even if it works)
- **Calling a package "broken" on weak evidence.** Package authors are the community. The wording is "suspicious", never "broken", and evidence is always shown.
- **Silent overrides.** A community override that changes a verdict without a visible label.
- **Guessing when offline or when a source fails.** It must say `unknown` with a reason, never `ok`.
- **Exit 0 in CI when it couldn't check.** That's a false green.
- **Running untrusted code.** Loading or executing any assembly from a nupkg.
- **Leaking secrets.** Feed credentials in logs, reports or PR comments.
- **Pasting package-controlled text unescaped** into markdown, PR comments or the backoffice.
- **Becoming a v17-only tool.** Any hard-coded major is a failure.
- **Noisy CI.** If the mf-harness advisory job produces noise people learn to ignore, it has failed.
- **Calling the Marketplace `validatePackage` endpoint.** It seems to trigger a re-sync [unverified]. Never call it.

---

## 3. Users and core use cases

### Users
- **Primary:** agency developers and upgrade planners (Manifesto first). They live in the terminal and CI.
- **Secondary:** package authors checking their own package before publishing.
- **Phase 3:** backoffice admins and developers on a running site.

### Core use cases
| # | Use case | Command |
|---|---|---|
| UC1 | Plan a v13 to v17 (or v17 to v18) upgrade | `scan ./MySite.sln --target 17` |
| UC2 | Pick the right patch inside a major | `scan . --target 17.7.2` |
| UC3 | CI on every PR touching packages | `scan . --target current --fail-on no-release` |
| UC4 | Audit 20 client sites at once, for quoting | `scan ~/Projects/* --target 18 --format md -o audit.md` |
| UC5 | Author self-check (secondary) | `explain My.Package 2.1.0 --target 18` |
| UC6 | See package health on a live site (phase 3) | Backoffice: Settings > Package health |

---

## 4. Constraints

### Versions
- **The CLI scans** projects on Umbraco 13 to 18+ and targets any major. No major is hard-coded.
- **CLI runtime:** `net8.0` with `RollForward=Major`, so it runs on the 8, 9 and 10 SDKs. Umbraco17Check does the same [verified].
- **Phase 3 backoffice package:** one build for v17 and v18, `Umbraco.Cms` range `[17.0.0, 19.0.0)`, per the portfolio rule. Split into two lines only if a spike proves the APIs force it.
- Re-check v19 (around Q4 2026 [unverified]) when it lands.

### Deadline
- **0.1 = M0 to M5, released before 14 Dec 2026** (Decision 3).
- MVP estimate: 2.5 to 3.5 weeks part-time [unverified estimate]. That leaves about 3 weeks of slack from 29 Oct.
- If time runs short, cut in this order: `--msbuild`, H11, H17, multi-site matrix rendering, SARIF. Never cut offline correctness, exit code 3 or the safety rules.

### Licence
- **Code:** MIT (tool, action, backoffice package).
- **Overrides data repo:** CC0 (from the scope doc).

### Security (non-negotiable)
- **Untrusted input:** every nupkg is hostile until proven otherwise.
  - Metadata reads only. Never `Assembly.Load`, `LoadFrom`, `MetadataLoadContext` with execution, or reflection-invoke.
  - Read from the zip stream. Don't extract to disk. If anything is extracted, reject `..`, absolute and rooted entry paths (zip-slip).
  - Caps: nupkg size 250 MB, 20,000 entries, 50 MB per inspected entry (architect's call on the last two).
- **Output escaping:** all package-controlled text (ids, summaries, override text) is escaped in markdown and console output. README text is never copied in.
- **Credentials:** feed credentials come only through NuGet credential providers and `NuGet.config`. Never logged, never in reports, never in the cache.
- **GitHub Action:** pin the tool to an exact version and the action by SHA. `pull_request` only, never `pull_request_target`. Least-privilege `permissions:`.
- **mf-harness:** the check is advisory. Making it blocking changes a control, which needs an ADR plus `security-analyst` sign-off (Decision 4).
- **Phase 3, per `~/.claude/references/umbraco-pitfalls.md`:**
  - **S1 [Critical]:** every API is a `ManagementApiControllerBase` with `[VersionedApiBackOfficeRoute]` and `[Authorize(Policy = AuthorizationPolicies.RequireAdminAccess)]`. No `UmbracoApiController`, no plain `ControllerBase`.
  - **P2 [Critical]:** no request-scoped object (`HttpContext`, `UmbracoContext`) in a static or singleton field.
  - **P7:** no lookups or scanning at startup. Everything runs on demand.
  - **S3-style XSS:** render package text as text in Lit. No `unsafeHTML`.
  - Outbound HTTP from production is opt-in by config. No user-supplied URL is ever fetched (no SSRF).

---

## 5. Decisions already made (Pascal, 2026-10-29)

| # | Decision |
|---|---|
| D1 | **Scope: packages only.** Link to Umbraco17Check for code rules |
| D2 | **Name:** `Umbraco.Community.PackageCheck`. Command: `umbraco-package-check` |
| D3 | **Deadline:** release 0.1 (M0 to M5) before v13 end of life on **14 Dec 2026** |
| D4 | **mf-harness:** add an advisory (warning-only) PR check **after the M6 backtest**. Never blocking without an ADR and security sign-off |
| D5 | **Overrides repo:** public from 0.1, with a co-maintainer. Pitch it in forum thread 7933 after the backtest |
| D6 | **Backoffice "Package health" view: yes**, planned in as phase 3, after the GitHub Action. Overrides the "only if asked" default |

**Portfolio rules (README) that also apply:**
- Pascal owns it, in his own GitHub and NuGet account. "Own it, announce it": post publicly what's being built, don't wait for approval.
- Neutral `Umbraco.Community.*` IDs. **Never rename an ID** once published.
- One build for v17 and v18 where possible.
- Reserve IDs early with a 0.0.1-alpha.
- Consider a co-maintainer to reduce bus-factor risk (D5 already requires one for the overrides repo).

**Related names (from the scope default, accepted with D2):**
- Action repo: `umbraco-package-check-action`.
- Overrides data repo: `umbraco-package-compat`.
- Phase 3 NuGet ID: `Umbraco.Community.PackageCheck.Backoffice` (architect's call; a dotnet tool package can't also be a site library, so it needs its own ID).

---

## 6. Functional spec

### 6.1 MVP (0.1): the dotnet tool

#### Commands
```
umbraco-package-check scan [paths...]
    --target <17 | 17.7.2 | current | next-major | latest-lts>   default: next-major
    --format console|json|md            default: console
    -o, --output <file>                 write json/md; console summary still printed
    --fail-on none|suspicious|upgrade|no-release                   default: none
    --transitive                        include transitive Umbraco packages (lock file or --msbuild)
    --msbuild                           exact versions via `dotnet list package --format json` (needs restore)
    --prerelease                        allow prerelease versions as an upgrade target
    --no-inspect                        metadata only; no nupkg download or inspection
    --offline                           cache, global packages folder and bundled snapshot only
    --overrides <file|url>              extra overrides on top of the community file
    --cache-dir <dir>  --max-age <6h>  --no-cache
    --verbose                           print every piece of evidence

umbraco-package-check explain <packageId> [version] --target <v>
umbraco-package-check cache clear|info
```

**Path handling:**
- Accepts `.sln`, `.slnx`, `.csproj` or a folder. Several paths allowed; shell globs expand them.
- A folder is searched for `.slnx`, then `.sln`, then `*.csproj`, up to 4 levels deep, skipping `bin`, `obj`, `node_modules` and `.git` (architect's call).
- Each top-level path is one **site** in the report. That's what the multi-site matrix groups by.
- If no project references any `Umbraco.Cms*` package, exit 2 with "no Umbraco project found".
- **Several paths (Pascal, 2026-10-30):** a path with no Umbraco project gives a warning ("no Umbraco project found in <path>; skipped") and is skipped. The run exits 2 only when no path has an Umbraco site. A path that doesn't exist is a usage error: exit 2.

**Target resolution:**
| Value | Resolves to |
|---|---|
| `17` | The latest stable `Umbraco.Cms` 17.x on the feed |
| `17.7.2` | Exactly that version |
| `current` | The site's own `Umbraco.Cms` version (the CI mode) |
| `next-major` | Current major + 1, latest stable in it. If none is released, exit 2 with a clear message |
| `latest-lts` | Latest LTS major, from `umbraco-versions.json` in the overrides release (see 7.8) |

- With several sites on different majors, `current` and `next-major` resolve **per site** (architect's call).
- **Inside one site, the site's Umbraco version** is the highest `Umbraco.Cms` (or `Umbraco.Cms.Core`) version across its projects. If projects disagree on the major, the report adds a site warning, "mixed Umbraco majors", and still uses the highest (architect's call). Each project keeps its own `umbracoVersion` in the report.
- The target's .NET TFM comes from the `Umbraco.Cms` nuspec dependency groups for that version. A bundled table is the fallback (architect's call).
- **Prerelease targets (architect's call):**
  - An explicit prerelease version is always allowed, for example `--target 18.0.0-rc2`. That's how UC1 and UC4 work before 18 ships.
  - A major-only target (`18`, `next-major`) resolves to the latest stable. If there is no stable in that major, it resolves to the latest prerelease **only with `--prerelease`**. Otherwise exit 2 with "no stable 18.x yet; pass --prerelease or an exact version".
  - The TFM still comes from the prerelease's own nuspec, so the fallback table is only for offline runs.
  - The resolved target is marked `"prerelease": true` in the report.
  - **Range checks use the release base (Pascal, 2026-10-30).** A prerelease target such as `18.0.0-rc2` counts as `18.0.0` when it's compared with a package's `Umbraco.Cms.*` range. So `[18.0.0, )` allows it and `[17.0.0, 18.0.0)` excludes it. Plain NuGet semantics would do the opposite and give false verdicts before 18 ships.
- **`--transitive` with no lock file and no `--msbuild` (architect's call):** the run continues with direct references, prints a warning, sets `summary.transitive: "unavailable"` and `summary.incomplete: true`. So with `--fail-on` set it exits 3, never a silent false green. If `--msbuild` is cut from 0.1, this is the only behaviour.

#### Verdicts
| Verdict | Meaning | Typical confidence |
|---|---|---|
| `ok` | Current version supports the target | high if range and contents agree; medium on range only |
| `upgrade` | A newer stable version supports the target. Report names it | as for `ok` |
| `no-release` | No stable version supports the target. Prereleases are mentioned | high when every range excludes the target |
| `suspicious` | Metadata says yes, contents say no | medium or low |
| `replaced` | Retired on the Marketplace, or deprecated on NuGet with an alternate | high |
| `unknown` | Private feed miss, not found, offline miss, unresolved property, conflicting evidence | n/a |
| `not-umbraco` | No path to `Umbraco.Cms.*` in its dependency tree | hidden unless `--verbose` |

**`upgrade` picks** the **highest stable** version that clears the target, within the lowest package major that does (architect's call: "6.3.3, not 7.x"). The report also lists the highest overall if it differs.

**Severity order for `--fail-on`** (worst first): `no-release` > `upgrade` > `suspicious`. `replaced` counts as `upgrade`. `unknown` never fails a run on its own, but a source failure does (exit 3).
- So `--fail-on suspicious` fails on `suspicious`, `upgrade`, `replaced` and `no-release` (architect's call: the thresholds nest).

**Confidence:**
- `high`: two independent kinds of evidence agree (metadata plus contents), or a curated override matches.
- `medium`: one strong signal (a strict upper-bounded range, or contents only).
- `low`: an open-ended range is the only evidence.
- An override is always labelled and linked. It never silently changes a verdict.

#### Acceptance criteria (0.1)

**Inputs**
- Given a `.slnx` with central package management, when I run `scan`, then versions come from `Directory.Packages.props` including `VersionOverride`, and test projects outside `src/` are included.
- Given a repo with `packages.lock.json` and `--transitive`, when I run `scan`, then transitive Umbraco packages (for example `Umbraco.Workflow.Core`) are listed with `direct: false`.
- Given a version set by an `$(Property)` that can't be resolved, when I run `scan`, then that package is `unknown` with reason "unresolved MSBuild property", and the run doesn't crash.
- Given a folder with no Umbraco project, when I run `scan`, then the exit code is 2 and the message says so.
- Given several paths where only some have an Umbraco project, when I run `scan`, then the others are skipped with a warning and the exit code follows the remaining sites. Given a path that doesn't exist, the exit code is 2.
- Given three site folders, when I run `scan a b c --format md`, then the report has a site x package matrix as well as per-package tables.

**Verdicts**
- Given Contentment 6.2.1 and `--target 17.7.2`, when I run `scan`, then the verdict is `upgrade` to 6.3.3, not 7.x.
- Given `astroboard 1.1.3`, `--target 17` and no matching override, then the computed verdict is `suspicious` with evidence "open range >= 13.8.1" and "legacy package.manifest only".
- Given `astroboard 1.1.3`, `--target 17` and the bundled seed override (`broken`), then the report shows the override's verdict `no-release`, the label "override", and `override.computedVerdict: "suspicious"`.
- Given `Umbraco.Community.SimpleDashboards 17.2.0`, then the verdict is `ok`, confidence `high`, through H09 evidence.
- Given `Umbraco.Workflow 17.5.4`, then the `umbraco-package.json` in its `Umbraco.Workflow.StaticAssets` dependency counts as evidence, and the verdict is `ok`.
- Given `Crumpled.RobotsTxt 3.1.1` (ships both manifests), then it is not `suspicious` because of its manifests.
- Given a package NuGet deprecated with an alternate, then the verdict is `replaced` and names the alternate.
- Given Skybrud Redirects with only a `17.1.0-beta` supporting the target, then it's offered only with `--prerelease`; without it, the verdict is `no-release` with the prerelease mentioned.
- Given an override entry that matches, then the report shows the entry, its evidence link and the label "override".
- Given an override past its `reviewBy` date, then it still applies but is marked "stale" (architect's call).

**CI**
- Given `--fail-on suspicious` and one `suspicious` package, then the exit code is 1.
- Given `--fail-on suspicious` and nuget.org unreachable, then the exit code is 3, not 0.
- Given `--fail-on none` and nuget.org unreachable, then the exit code is 0 and `summary.incomplete` is `true`.
- Given `--offline` and a warm cache, then no network call is made. A test asserts the HTTP handler is never hit.
- Given `--offline` and a cold cache, then missing packages are `unknown` with reason `offline`.

**Safety**
- Given any nupkg, then no assembly from it is loaded or run.
- Given a nupkg with a `../evil` entry, then that entry is rejected and the package is `unknown` with reason "unsafe archive".
- Given a nupkg over the size cap, then it isn't downloaded or opened, and the package falls back to metadata-only evidence.
- Given a package id containing markdown or HTML, then the md report shows it escaped.
- Given a private feed with credentials, then no credential appears in console, JSON, md, logs or cache files.

**Packaging**
- Given a clean machine with the .NET 8, 9 or 10 SDK, when I run `dotnet tool install -g Umbraco.Community.PackageCheck`, then `umbraco-package-check --version` works.

### 6.2 Phase 2: GitHub Action

**Shape:**
- A **composite action** in its own repo, `umbraco-package-check-action`.
- Installs the tool at a **pinned exact version** (an input with a default baked into each action release), then runs `scan`.
- Writes the md report to `$GITHUB_STEP_SUMMARY`.
- Keeps **one sticky PR comment** up to date (found by a hidden HTML marker), when `comment: true`.
- Optional SARIF upload (`sarif: true`), so annotations land on the `Directory.Packages.props` or `.csproj` line. SARIF is a new `--format sarif` in the tool (architect's call: SARIF belongs in the tool, not the action).

**Inputs:**
| Input | Default |
|---|---|
| `path` | `.` |
| `target` | `current` |
| `fail-on` | `none` |
| `overrides` | empty |
| `comment` | `true` |
| `sarif` | `false` |
| `tool-version` | the version pinned by this action release |

**Outputs:** `exit-code`, `report-json` (path), `counts` (JSON).

**Recommended triggers (documented in the README):**
- `pull_request` with a paths filter: `**/*.csproj`, `**/Directory.Packages.props`, `**/Directory.Build.props`, `**/packages.lock.json`, `**/NuGet.config`.
- Weekly `schedule` with `target: next-major`, to catch new releases and retirements.

**Permissions:** `contents: read`; `pull-requests: write` only if `comment: true`; `security-events: write` only if `sarif: true`.

**Fork PRs:** no secrets are available, so private-feed packages come out `unknown`. The comment step skips quietly if the token can't write.

**mf-harness (Decision 4, after M6):**
- Add an `umbraco-packages` job to mf-harness `pr-checks.yml` with `needs: changes` [verified job list].
- Runs only when the repo is Umbraco and dependency files changed.
- `continue-on-error: true`, and **not** in the aggregate `pr-checks` job's `needs`. Same pattern as `ai-review`.
- Reuse `pr-checks/nuget-private-feed.txt` so private ids come out `unknown`, not errors.
- Keep consistent with `pr-checks/nuget.py`, which already reads CPM without MSBuild.
- That change is a PR to mf-harness, not part of this package's repo.

**Acceptance criteria (phase 2):**
- Given a PR that changes `Directory.Packages.props`, when the workflow runs, then the step summary shows the md report and one PR comment is created.
- Given a second push to the same PR, then the same comment is updated, not duplicated.
- Given `fail-on: no-release` and a package with no release, then the job fails with exit code 1.
- Given `fail-on: suspicious` and nuget.org down, then the job fails with exit code 3.
- Given a PR from a fork, then the job completes without a write-permission error.
- Given `sarif: true`, then an annotation appears on the line that declares the flagged package.
- Given the mf-harness advisory job and a `no-release` finding, then the PR shows a warning but the `pr-checks` aggregate still passes.
- Given 2 weeks on 2 repos, then no finding is noise (M7 done-when).

### 6.3 Phase 3: backoffice "Package health" view

**What it adds over the CLI: runtime truth.** It sees what actually loaded on the running site, with no csproj.

**Shape (architect's call, since D6 says "yes" but not which form):**
- A separate NuGet package, `Umbraco.Community.PackageCheck.Backoffice`, for v17 and v18 in one build.
- A **dashboard in the Settings section** called "Package health". Admin only.
- **Plus** an Umbraco **Health Check** ("Package health" group) that flags leftover legacy `App_Plugins` folders and legacy-only manifests. Cheap, and it shows in the standard Health Check dashboard.
- It reuses `PackageCheck.Core`, `.Inspect` and `.Sources` from the tool. No second engine.

**Three tiers of data:**
1. **Always, no network:**
   - Installed packages and versions, from the app's `DependencyContext` (`*.deps.json`). Filter to packages with a path to `Umbraco.Cms.*`.
   - Loaded manifests, from the same service `GET /umbraco/management/api/v1/manifest` uses (`AllManifestController`, `BackOfficeAccess`) [verified, 17.7.2 source]. That includes C# `IPackageManifestReader` manifests.
   - Leftover legacy `App_Plugins/*/package.manifest` folders in the web root (H17).
2. **On demand, opt-in network:** an admin clicks "Check against target" (current, next major, or a typed version). The server runs the verdict engine against NuGet and the Marketplace, with the same cache rules. Disabled unless `PackageCheck:AllowOutbound` is `true` in config.
3. **Import a CI report:** paste or upload a `schemaVersion: 1` JSON report and view it. No network needed. **Stretch** for phase 3.

**Config (`appsettings.json`, section `PackageCheck`):**
- `AllowOutbound` (bool, default `false`, architect's call: secure by default, and tiers 1 and 3 work without it).
- `OverridesUrl` (default: the community release asset). Config only, never set from the UI.
- `CacheMinutes` (default 360).

**Acceptance criteria (phase 3):**
- Given a v17 site with Contentment and SimpleDashboards installed, when an admin opens Settings > Package health, then both appear with installed versions and "manifest loaded: yes".
- Given a site with a leftover `App_Plugins/OldThing/package.manifest`, when the Health Check runs, then it reports a warning naming the folder.
- Given a non-admin backoffice user, when they call the Package health API, then they get 403.
- Given an anonymous request to the API, then it gets 401.
- Given `AllowOutbound: false`, when an admin opens the dashboard, then no outbound HTTP is made and the "Check against target" button explains how to enable it.
- Given `AllowOutbound: true`, when an admin clicks "Check against target" with 18, then verdicts appear with evidence, and a second click within the cache time makes no outbound call.
- Given site startup, then the package does no network or scanning work (P7).
- Given a package summary containing `<script>`, then the dashboard shows it as text.
- Given the same nupkg built once, when it's installed on v17 and on v18, then the dashboard works on both.

---

## 7. Technical design

### 7.1 Solution layout
```
UmbracoPackageCheck.sln (or .slnx)
  src/PackageCheck.Core        models, verdict engine, IHeuristic (one class per H-id), confidence rules
  src/PackageCheck.Inputs      sln/slnx/csproj/props/lock readers; optional `dotnet list package` runner
  src/PackageCheck.Sources     NuGet (NuGet.Protocol), Marketplace client, overrides loader, cache
  src/PackageCheck.Inspect     nupkg reader (NuGet.Packaging), metadata scan (System.Reflection.Metadata)
  src/PackageCheck.Report      json, md, console (and later sarif) renderers; the JSON Schema file
  src/PackageCheck.Cli         System.CommandLine; packed as the dotnet tool
  src/PackageCheck.Backoffice  phase 3: Umbraco package (C# + Lit client in Client/)
  tests/...                    see section 9
```
- Core, Inputs, Sources, Inspect and Report target `net8.0` so both the tool and the net10 backoffice package can use them (architect's call).
- Only `PackageCheck.Cli` and `PackageCheck.Backoffice` are published to NuGet. The libraries are bundled inside them, not published separately (architect's call; avoids extra IDs to maintain).

### 7.2 Key components and interfaces

Sketches, not final signatures. Names are the builder's to refine.

```csharp
// Inputs
interface IProjectReader {                 // sln, slnx, csproj, folder
    Task<ScanInput> ReadAsync(string path, InputOptions o, CancellationToken ct);
}
record ScanInput(string SitePath, IReadOnlyList<ProjectInfo> Projects);
record ProjectInfo(string Path, IReadOnlyList<string> Tfms, NuGetVersion? UmbracoVersion,
                   IReadOnlyList<PackageRef> Packages);
record PackageRef(string Id, string? VersionText, NuGetVersion? Version, bool Direct, string? UnresolvedReason);

// Sources
interface IPackageMetadataSource {         // NuGet registration + flat container, via NuGet.Protocol
    Task<PackageVersions> GetVersionsAsync(string id, CancellationToken ct);
    Task<PackageDetails?> GetDetailsAsync(string id, NuGetVersion v, CancellationToken ct); // deps per TFM, deprecation, published
    Task<Stream?> OpenNupkgAsync(string id, NuGetVersion v, CancellationToken ct);          // global packages folder first
}
interface IMarketplaceSource { Task<MarketplaceInfo?> GetAsync(string id, NuGetVersion target, CancellationToken ct); }
interface IOverrideSource    { Task<OverrideSet> LoadAsync(CancellationToken ct); }

// Inspect
interface INupkgInspector { Task<InspectionResult> InspectAsync(PackageIdentity id, Stream nupkg, CancellationToken ct); }
record InspectionResult(bool HasUmbracoPackageJson, string? StaticWebAssetBasePath, bool HasLegacyManifest,
                        bool ReferencesManifestReader, bool ReferencesManagementApi, bool HasClientAssets,
                        IReadOnlyList<string> LibTfms, string ContentHash);

// Core
interface IHeuristic {
    string Id { get; }                     // "H01" ...
    Task<IEnumerable<Evidence>> EvaluateAsync(HeuristicContext ctx, CancellationToken ct);
}
record Evidence(string Check, EvidenceResult Result, EvidenceWeight Weight, string Source, string Detail);
interface IVerdictEngine { Task<PackageVerdict> EvaluateAsync(PackageRef pkg, TargetVersion target, CancellationToken ct); }
```

**Engine flow per package:**
1. Resolve the current version's details. If no path to `Umbraco.Cms.*` (H04, depth 3), mark `not-umbraco`.
2. Run metadata heuristics for the current version against the target.
3. Unless `--no-inspect`, inspect the current nupkg and its Umbraco dependencies (needed for Workflow's StaticAssets).
4. Combine evidence into a verdict and confidence (rules in 7.4).
5. If not `ok`, walk newer versions (stable only unless `--prerelease`), lowest-major-first, to find the `upgrade` candidate. Inspect only the chosen candidate (architect's call: keeps downloads low).
6. Apply overrides last, labelled.

### 7.3 Data sources and APIs

**NuGet** [verified endpoints]:
- Use `NuGet.Protocol`, `NuGet.Versioning` and `NuGet.Packaging`, not hand-rolled HTTP. It honours `NuGet.config`, source mapping and credential providers, so private feeds come almost free, and range maths is exact.
- Resolve resources from the service index `https://api.nuget.org/v3/index.json`. Don't hard-code URLs.
- Registration (`registration5-gz-semver2`): every version, `listed`, `dependencyGroups` per TFM, `deprecation`, `published`. Pages may be inline or linked.
- Flat container: versions index (includes unlisted), `.nuspec`, `.nupkg`.
- Search API: not needed.
- Pick the dependency group NuGet would choose for the target TFM (`NuGetFrameworkUtility.GetNearest`).
- Feed config comes from the nearest `NuGet.config` to each site, via `Settings.LoadDefaultSettings(root)`.

**Umbraco Marketplace** [verified unless tagged]:
- OpenAPI: `https://api.marketplace.umbraco.com/openapi/v1.json`. Not a documented contract, so it's **optional**: 24h cache, quiet degrade on any error, never causes exit 3.
- Detail: `GET /api/v1.0/packages/{id}?SupportsUmbracoVersionNumber=<target>`.
  - Useful fields: `latestVersionNumberForProvidedUmbracoVersion`, `packageIdForProvidedUmbracoVersion`, `versionDetails[]`, `versionSpecificPackageIds`, `isRetired`, `alternatePackageIdForRetiredPackage`, `isHQ`, `sourceCodeRepositoryUrl`, `issueTrackerUrl`.
- Used for renames, retirements and the HQ trust signal. **Not** independent compatibility evidence (it repeats NuGet ranges).
- **Never call** `GET /api/v1.0/validatePackage/{id}`.

**Overrides:** the `umbraco-package-compat` repo's latest release asset `overrides.json` plus its sha256 (7.8).

**HTTP rules:** concurrency 8, retry with backoff on 429 and 5xx (max 3 tries), timeout 30s per call, `User-Agent: umbraco-package-check/<version> (+repo URL)`.

**What counts as "a source failed" (exit 3), architect's call:**
- Judged **per package**. One failed package makes the whole run `incomplete`.
- **Source error (counts):** DNS or connection failure, timeout, or 5xx after retries, from any feed the package needed. The package is `unknown`, reason `source-error`.
- **Not a source error (doesn't count):**
  - 404 on every configured feed: `unknown`, reason `not-found`.
  - 401 or 403 from a feed (no or bad credentials): `unknown`, reason `private-feed`.
  - An id listed in a private-feed ignore list (for mf-harness, `pr-checks/nuget-private-feed.txt`, passed with `--private-ids <file>`): skipped as `unknown`, reason `private-feed`, with no HTTP call.
- Marketplace and overrides fetch failures never count. They fall back to cache or the bundled snapshot with a warning.

### 7.4 Heuristics catalogue

Each check adds weighted evidence. The verdict comes from combining them, never from one rule.

| ID | Check | Source | In 0.1? | Signal |
|---|---|---|---|---|
| H01 | Declared `Umbraco.Cms.*` range allows the target, using the dependency group for the target TFM | Registration / nuspec | Yes | medium if bounded, low if open |
| H02 | Range has an upper bound that excludes the target | Same | Yes | high negative |
| H03 | Open range starts below 14 while the target is 14+ | Same | Yes | low; raises `suspicious`, never `no-release` |
| H04 | Transitive walk to `Umbraco.Cms.*`, depth 3, when there's no direct dependency (63 of 517 v17-badged packages need it [verified]) | Registration | Yes | inherits H01 |
| H05 | Depends on a core package removed in the new backoffice (e.g. `Umbraco.Cms.Web.BackOffice`) | Registration | Yes | high negative |
| H06 | TFM fit: `lib/` folders vs the target's .NET | Nuspec, nupkg | Yes | low alone; strengthens H03 |
| H07 | `staticwebassets/**/umbraco-package.json` in this package **or a dependency**; `BasePath` from `build/Microsoft.AspNetCore.StaticWebAssets.props` | nupkg | Yes | high positive |
| H08 | Legacy `package.manifest` only (under `content/App_Plugins`, `App_Plugins/` or `staticwebassets/`), with no H07 or H09. Ignored when H07 is present (dual-mode, e.g. `Crumpled.RobotsTxt 3.1.1` [verified]) | nupkg | Yes | high negative for target 14+ |
| H09 | Assembly type-references `IPackageManifestReader` or `PackageManifest` (in `Umbraco.Cms.Infrastructure.Manifest`), in this package or a dependency (SimpleDashboards uses `jcdcdev.Umbraco.Core` [verified]) | `System.Reflection.Metadata`, no load | Yes | medium-high positive |
| H10 | References to types or members removed in the target | API index from `Umbraco.Cms.*` nupkgs | **No, M8** | high negative when exact |
| H11 | References Management API types but ships no JS and no manifest | nupkg | Yes (cuttable) | low |
| H12 | Marketplace cross-check of latest version for the target | Marketplace | Yes | low; not independent |
| H13 | Retired or renamed on the Marketplace | Marketplace | Yes | high when present |
| H14 | NuGet deprecation plus alternate | Registration | Yes | high |
| H15 | Only a prerelease supports the target | Flat container | Yes | info only |
| H16 | Community or local override matches id, version range and target | Overrides | Yes | high, always labelled |
| H17 | Leftover legacy `App_Plugins/<name>` in the **project** that shadows a package's `BasePath` | Project file system | Yes (cuttable) | medium; warn only on a name match |
| H18 | Last release older than the target major | Registration `published` | Yes | info only |
| H19 | HQ package (`isHQ`) with a strict range raises H01 to high | Marketplace | Yes | modifier |

**Applicability:** H03, H07, H08, H09 and H11 only matter when the target is 14+ (new backoffice). For targets of 13 or below, they're skipped (architect's call).

**Combining rule (default):**
- Any high negative (H02, H05, H08, H10) beats positive metadata.
  - `no-release` when no version clears the negative.
  - `suspicious` when only the contents disagree with the metadata.
- `ok` with `high` confidence needs H01 plus one of H07, H09 or H16.
- `ok` with H01 alone is `medium` (bounded) or `low` (open range).
- Server-only packages (no client assets, no manifest, no Management API refs) aren't penalised by H08 absence rules: missing H07/H09 alone never makes `suspicious` (architect's call, from the Workflow and SimpleDashboards caveat).
- Conflicting high evidence (a high positive and a high negative) gives `unknown`, reason "conflicting evidence", and shows both. This applies to heuristic evidence only (H01 to H15, H17 to H19), not to overrides.

**Truth table for the current version (architect's call).** Read top to bottom; the first matching row wins. "Contents" means H07/H08/H09 results; it's "n/a" with `--no-inspect` or for server-only packages.

| # | Range for target (H01/H02) | Other high negative (H05, H08) | High positive (H07, H09) | Verdict | Confidence |
|---|---|---|---|---|---|
| 1 | excludes (H02) | any | any | `upgrade` if a newer version clears, else `no-release` | high |
| 2 | allows | yes | yes | `unknown`, "conflicting evidence" | n/a |
| 3 | allows, bounded | yes | no | `suspicious` | medium |
| 4 | allows, open (H03) | yes | no | `suspicious` | medium |
| 5 | allows, bounded or HQ (H19) | no | yes | `ok` | high |
| 6 | allows, open | no | yes | `ok` | high |
| 7 | allows, bounded | no | no / n/a | `ok` | medium |
| 8 | allows, open from 14+ | no | no / n/a | `ok` | low |
| 9 | allows, open from below 14, target 14+ (H03) | no | no / n/a | `suspicious` if H06 also fails (no target-major TFM), else `ok` | low |
| 10 | no `Umbraco.Cms` path (H04) | n/a | n/a | `not-umbraco` | n/a |

- H11, H12, H15, H17 and H18 only add evidence lines. They never change the verdict.
- H13 and H14 turn any row into `replaced` (high), naming the alternate, unless row 1 or 5-8 says the current version is fine; then they add a note only.
- The same table is used to judge each `upgrade` candidate: a candidate "clears" when it lands on rows 5 to 8.
- The golden fixtures in section 9 must all agree with this table. If a fixture and the table disagree during M0, the builder fixes the table and records why in the repo.

**Overrides (H16) are applied after the table (architect's call):**
- A matching override **replaces** the computed verdict, including `unknown` from conflicting evidence. That's what overrides are for (the astroboard-style cases).
- The report keeps the computed verdict in `override.computedVerdict`, shows the override's evidence link, and labels it. Nothing is silent.
- Local `--overrides` entries win over community entries.

### 7.5 Caching and offline mode

**Lookup order for a nupkg:**
1. NuGet global packages folder (`~/.nuget/packages/{id}/{v}/`, or `NUGET_PACKAGES`). On a restored repo most are here.
2. Inspection cache (below).
3. Download from the feed, inspect from a temp file, delete the temp file.

**Cache:** `LocalApplicationData/umbraco-package-check/` (override with `--cache-dir`).
| What | Key | TTL |
|---|---|---|
| Registration JSON | feed + id | 6h (`--max-age`) |
| Marketplace JSON | id + target | 24h |
| Overrides file | URL | 24h, hash-checked |
| **Inspection result** | id + version + contentHash | forever (published nupkgs don't change). Store the small evidence JSON, not the nupkg |

- Cache files are written atomically (write temp, then rename) so parallel runs don't corrupt them (architect's call).
- Cache entries never hold credentials or auth headers.
- Bump a `cacheVersion` in the inspection cache key whenever a heuristic changes, so old evidence is discarded (architect's call).

**Offline mode (`--offline`):**
- Uses only the cache, the global packages folder and the bundled overrides snapshot.
- Anything missing becomes `unknown`, reason `offline`. It never guesses.
- No HTTP handler is ever invoked. Enforced by a test.

### 7.6 Report schema (JSON, `schemaVersion: 1`)

Ship a JSON Schema file in the repo and in the nupkg. Publish it at a stable URL from the repo.

```jsonc
{
  "schemaVersion": 1,
  "tool": { "name": "umbraco-package-check", "version": "0.1.0" },
  "generatedAt": "2026-12-01T10:00:00Z",
  "options": { "failOn": "none", "offline": false, "transitive": false, "inspect": true, "prerelease": false },
  "sites": [
    {
      "path": "./MySite",
      "target": { "requested": "next-major", "resolved": "18.2.0", "tfm": "net10.0" },
      "projects": [ { "path": "src/Web/Web.csproj", "umbracoVersion": "17.7.2", "tfms": ["net10.0"] } ],
      "packages": [
        {
          "id": "astroboard",
          "current": "1.1.3",
          "direct": true,
          "projects": ["src/Web/Web.csproj"],
          "verdict": "suspicious",           // ok|upgrade|no-release|suspicious|replaced|unknown|not-umbraco
          "confidence": "medium",            // high|medium|low|null
          "reason": null,                    // set for unknown: offline|unresolved-property|private-feed|not-found|unsafe-archive|conflicting-evidence|source-error
          "recommended": null,               // version string for upgrade; alternate id for replaced
          "highestCompatible": null,
          "prereleaseCompatible": null,
          "evidence": [
            { "check": "H03", "result": "negative", "weight": "low", "source": "nuget:registration", "detail": "open range >= 13.8.1" },
            { "check": "H08", "result": "negative", "weight": "high", "source": "nupkg", "detail": "legacy package.manifest only" }
          ],
          "marketplace": { "url": "https://marketplace.umbraco.com/package/astroboard", "retired": false, "alternate": null },
          "override": null                    // { id, status, summary, evidence, verifiedOn, reviewBy, stale }
        }
      ]
    }
  ],
  "summary": { "counts": { "ok": 0, "upgrade": 0, "no-release": 0, "suspicious": 1, "replaced": 0, "unknown": 0 }, "incomplete": false }
}
```

- **Note the change from the scope doc:** the scope had top-level `target`, `inputs[]`, `projects[]`, `packages[]`. This design nests them under `sites[]`, because `current` and `next-major` resolve per site and the multi-site matrix needs it (architect's call). A single-site run is just one entry.
- **What's versioned:** the JSON shape. Adding optional fields is not a breaking change. Removing or renaming a field, or changing an enum value, bumps `schemaVersion`.
- **What's not versioned:** markdown and console layout, evidence `detail` wording, the heuristic set. Consumers should key on `verdict` and `check` ids, not on text.

**Markdown:** one summary line; a table per verdict, worst first; a collapsible `<details>` with evidence per package; with more than one site, a site x package matrix first. All package text escaped. Safe to paste into a PR comment.

**Console:** the same tables, coloured (respect `NO_COLOR`), plus the top 3 actions.

### 7.7 Exit codes
| Code | Meaning |
|---|---|
| `0` | Nothing at or above `--fail-on` (always 0 with `--fail-on none`, even if incomplete) |
| `1` | Findings at or above `--fail-on` |
| `2` | Usage error, or no Umbraco project found, or the target can't be resolved because the input is invalid (e.g. no stable 18.x without `--prerelease`, or an unparseable version) |
| `3` | Incomplete: a required source failed while `--fail-on` was set. Fails closed in CI |

- If both 1 and 3 apply, return **3** (architect's call: the incomplete run is the bigger problem in CI).
- If the target can't be resolved because a feed is unreachable, rather than because the input is invalid, return **3**, not 2. This was aligned with the plan on 2026-10-30, after plan review P12, so CI fails closed on source outages.
- Unhandled exceptions return 2 with a one-line message. Stack trace only with `--verbose` (architect's call).

### 7.8 Overrides repo format (`umbraco-package-compat`)

**Layout:**
```
overrides/<package-id-lowercase>.yml   one file per package (fewer merge conflicts)
schema/override.schema.json            JSON Schema for one file
umbraco-versions.json                  majors, LTS flag, EOL date, TFM (feeds --target latest-lts)
.github/workflows/build.yml            validate every PR; on tag, compile overrides.json + sha256
LICENSE                                CC0
CONTRIBUTING.md                        governance rules below
```

**Entry format:**
```yaml
id: Astroboard
entries:
  - packageVersions: "[1.0.0, )"       # NuGet range
    umbraco: "[14.0.0, )"              # NuGet range of Umbraco versions this entry is about
    status: broken                     # works | broken | partial
    summary: "Legacy AngularJS package.manifest only; does not load in the new backoffice"
    evidence: https://github.com/<owner>/<repo>/issues/<n>   # required, public
    reportedBy: "<github-handle>"
    verifiedOn: 2026-10-29
    reviewBy: 2027-10-29               # shown as stale after this date (default 12 months)
    replacement: null                  # optional package id
```

**Mapping to verdicts (architect's call):** `works` → `ok` high; `broken` → `no-release` if no other version clears, else `upgrade`; `partial` → `suspicious` with the summary. Always labelled.

**Before the repo exists (architect's call):** the builder writes a seed `data/overrides.seed.json` and `data/umbraco-versions.json` in the tool repo during M2. They hold astroboard plus the Umbraco majors 13 to 18. `latest-lts` and the offline tests use them from M2 on. In M5 the seed moves to `umbraco-package-compat` and the tool switches to the release asset, keeping the seed as the bundled snapshot.

**Release asset:** `overrides.json` (all files compiled, plus `umbraco-versions.json` embedded) and `overrides.json.sha256`. The tool fetches the latest release, checks the hash, caches 24h. Each tool release bundles a snapshot for offline use.

**Local overrides (`--overrides`):** same entry format, as a single YAML or JSON file or URL. Local entries win over community entries for the same id and range, and are labelled "local override".

**Governance (from the scope doc, D5):**
- Changes by PR only. Every entry needs a public evidence link.
- For `broken`, raise an issue on the package's repo first.
- Authors can claim their file. Their `works` wins unless there's an open `broken` report with a repro.
- Entries expire on `reviewBy` or at the package's next major.
- Merging needs two maintainers: Pascal plus one community co-maintainer.
- Public from 0.1. Pitch it in forum thread 7933 after the M6 backtest. Offer it to the Community Package Team later.

### 7.9 Phase 3 backoffice components
- **C#:**
  - `PackageHealthController : ManagementApiControllerBase`, `[VersionedApiBackOfficeRoute("package-check")]`, `[ApiExplorerSettings(GroupName = "Package Check")]`, `[Authorize(Policy = AuthorizationPolicies.RequireAdminAccess)]`.
    - `GET installed` (tier 1), `POST check` with `{ target }` (tier 2, 403 with a clear message if outbound is off), `POST import` (tier 3, stretch; size-capped, schema-validated).
  - `IPackageHealthService` (scoped or transient, not holding request state).
  - `LegacyAppPluginsHealthCheck : HealthCheck`.
  - Register through an `IComposer`. No work in the composer beyond DI registration.
- **Client:** Lit dashboard registered by `umbraco-package.json` (dogfoods H07), conditioned to the Settings section. Uses the generated OpenAPI client.
- **Engine reuse:** the same `IVerdictEngine`, with `IProjectReader` replaced by a `RuntimePackageReader` over `DependencyContext`.

---

## 8. Spikes to run first (M0)

Time-box: 2 days total for S1 to S4. S5 and S6 are short checks. S7 runs before phase 3 starts, not in M0.

| # | Spike | Decides |
|---|---|---|
| S1 | `NuGet.Protocol` range checks: registration fetch, pick dependency group for the target TFM, H01/H02/H04 for Contentment 6.2.1 → 6.3.3 at 17.7.2 | Confirms option (b) over hand-rolled HTTP. If `NuGet.Protocol` can't do lowest-major-highest-patch cleanly, write that selection by hand |
| S2 | Inspect astroboard 1.1.3, SimpleDashboards 17.2.0 and Workflow 17.5.4 (+ StaticAssets) from the zip stream; read `BasePath` from the props | Whether H07 and H08 need full dependency walks in 0.1, and the exact paths to match |
| S3 | `System.Reflection.Metadata` type-reference scan for `IPackageManifestReader` in SimpleDashboards and `jcdcdev.Umbraco.Core`, without loading | Whether H09 is reliable enough for `high` confidence, or stays `medium-high` |
| S4 | Read `.slnx` with `Microsoft.VisualStudio.SolutionPersistence` [unverified fit] on 2 of our `.slnx` repos | Use the library, or a plain XML reader |
| S5 | Marketplace: does it list a `DotnetTool` package type as a `Tool`? Which tag and `umbraco-marketplace.json` fields does it need? [unverified] | Release metadata in M5; whether the 0.0.1-alpha should carry the tag |
| S6 | Freshness re-check: Umbraco17Check releases, any HQ upgrade-readiness tool, forum thread 7933 | Positioning. If a rival now inspects nupkgs, sharpen on CI, private feeds, overrides and multi-site |
| S7 (pre-phase-3) | In a v17 and a v18 site: reference `NuGet.Protocol` inside the site (assembly conflicts?), read `DependencyContext`, call the manifest service, register a Health Check | One build `[17, 19)` vs two lines; whether tier 2 can run in-process |

**M0 is done when** the 4 golden verdicts print: astroboard `suspicious`, SimpleDashboards `ok`, Workflow `ok`, Contentment `upgrade` to 6.3.3. These are computed verdicts, with no overrides applied.

### Milestones (from the scope doc)
| # | Milestone | Done when |
|---|---|---|
| M0 | Spikes S1 to S6 | 4 golden verdicts print |
| M1 | Inputs: sln, slnx, csproj, CPM, `VersionOverride`, lock v2 | 10 repo-shape fixtures pass |
| M2 | Metadata verdicts (H01-H05, H12-H15, H19), `--target` resolution | `ok`, `upgrade`, `no-release`, `replaced` criteria pass |
| M3 | Inspection (H06-H09, H11, H17), cache, offline | `suspicious` criteria pass; offline makes no HTTP |
| M4 | Reports, exit codes, tool packaging | `dotnet tool install` works; schema published |
| M5 | Overrides repo, snapshot, README. **Release 0.1** | On NuGet and listed on the Marketplace |
| M6 | Backtest on the 10 sites | Precision and recall in the README |
| M7 | GitHub Action + mf-harness advisory job | 2 weeks on 2 repos with no noise |
| M8 | H10 removed-API index (stretch) | Finds `UmbracoAuthorizedApiController` users |
| M9 | Phase 3 backoffice package (S7 first) | Phase 3 criteria pass on v17 and v18 |

---

## 9. Testing strategy

- **Unit, one test class per heuristic.**
  - Build tiny synthetic nupkgs in the tests (nuspec plus chosen files).
  - Cover: manifest at root and under `App_Plugins`; dual-mode; props `BasePath`; type references (stub assemblies compiled once and checked in).
- **Verdict engine:** table-driven tests over evidence combinations, including conflicting evidence and every `--fail-on` threshold.
- **Golden fixtures: real packages, pinned by id, version and contentHash.**
  - Golden tests run with an empty override set, so they prove the computed verdict.
  - `astroboard 1.1.3` → `suspicious` (computed). One separate end-to-end test loads the seed override and expects `no-release`, label "override", `computedVerdict: "suspicious"`.
  - `Crumpled.RobotsTxt 3.1.1` → not `suspicious` on manifests.
  - `Umbraco.Community.SimpleDashboards 17.2.0` → `ok` via H09.
  - `Umbraco.Workflow 17.5.4` → `ok` via H07 in a dependency.
  - Contentment 4.7.0, 6.3.3 and 7.2.1, plus the `Our.Umbraco.Community.Contentment` rename.
  - Skybrud Redirects `17.1.0-beta` → only with `--prerelease`.
  - `MediaWiz.Forums 14.0.0` (`>= 14.0.0`, net8 only).
  - One NuGet-deprecated package with an alternate; one Marketplace-retired package.
  - Real nupkgs are **not** committed to the repo (licences vary). Tests download them by hash on first run into a test cache, and CI caches that (architect's call). Offline unit tests use synthetic nupkgs only.
- **Recorded HTTP:** NuGet and Marketplace responses replayed from fixtures through a fake `HttpMessageHandler`. CI is deterministic and offline. A weekly live job re-records and shows the diff.
- **Input fixtures from our 10 v17 repos:** shape only (csproj, props, lock skeletons with ids and versions). No code, no `appsettings`, no client names. Cover CPM, `.slnx`, lock v2, tests outside `src/`, a site under a `cms/` folder, `VersionOverride`.
- **Security tests:** zip-slip entry, oversize nupkg, entry-count bomb, markdown injection in ids and summaries, credentials never in output (scan all outputs for a planted fake token), assembly never loaded (assert `AppDomain` assembly list unchanged).
- **Contract tests:** every JSON report validates against the published schema. Markdown snapshot tests (Verify or similar).
- **CLI tests:** exit codes for every row in 7.7, run in-process.
- **Packaging test in CI:** pack, install the tool from the local nupkg on SDK 8, 9 and 10, run `--version` and one offline scan.
- **Backtest (M6), the real proof:**
  - Run `--target 17.7.2` on each of the 10 sites' pre-upgrade commits.
  - Compare with what the upgrade guide chose: Contentment 6.3.3, GMaps 17.3.1, Skybrud 17.0.5, BlockPreview 5.6.0; Workflow and Engage held back; leftover `App_Plugins` on one site.
  - Report precision and recall in the README. That number decides whether CI may ever block (D4).
- **Phase 3:** unit tests for the service; an integration test with `Umbraco.Cms.Tests.Integration` (or a booted test site) on v17 and v18 for the 401/403 rules; a Playwright smoke test of the dashboard.
- **Optional later:** a nightly smoke lab that installs flagged packages on a clean v17 site and calls the manifest endpoint, as ground truth for overrides.

---

## 10. Release and distribution

### NuGet
- Account: Pascal's own NuGet account. Add a co-owner later if a co-maintainer joins.
- Enable package signing via NuGet's repository signing (automatic) and publish from CI with an API key scoped to `Umbraco.Community.PackageCheck*` only (architect's call). Prefer NuGet trusted publishing from GitHub Actions if available on the account [unverified].
- SourceLink and deterministic builds on. Include the README and an icon in the nupkg.
- Versioning: SemVer. `0.x` while the schema and heuristics settle. `1.0` after the backtest and a quiet M7.

### dotnet tool packaging
```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>umbraco-package-check</ToolCommandName>
<PackageId>Umbraco.Community.PackageCheck</PackageId>
<TargetFramework>net8.0</TargetFramework>
<RollForward>Major</RollForward>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageTags>umbraco umbraco-marketplace upgrade compatibility dotnet-tool cli</PackageTags>
```
- Bundle the overrides snapshot and the JSON Schema as embedded resources.
- Install: `dotnet tool install -g Umbraco.Community.PackageCheck`, or as a local tool via a tool manifest for CI.

### Reserving the ID with 0.0.1-alpha
- Publish `0.0.1-alpha` as a real `DotnetTool` package as early as possible in M0, with the final `PackageId` and `ToolCommandName`. Prerelease versions are hidden from default search, so it doesn't look like a release.
- It prints "Umbraco Package Check: coming soon, see <repo URL>" and exits 2.
- Same metadata as the real release (licence, repo URL, README stating it's a placeholder).
- Leave it listed (architect's call; unlisting is fine too, the ID stays reserved either way).
- Reserve `Umbraco.Community.PackageCheck.Backoffice` the same way with a `0.0.1-alpha` library package that has no Umbraco code (architect's call; the ID is permanent, so claim it now).
- Don't apply for a NuGet prefix reservation on `Umbraco.Community.*`. HQ hasn't reserved it and other people own packages under it [verified], so it isn't ours to claim.

### Marketplace listing
- The Marketplace lists NuGet packages that carry the `umbraco-marketplace` tag [unverified, check in S5].
- Add `umbraco-marketplace.json` in the repo root for category, author, links and screenshots [unverified format, check in S5].
- Package type: **Tool** [verified: the Marketplace has a `Tool` type].
- Suggested tags: `upgrade`, `compatibility`, `cli`, `ci`, `tool`, `packages`.
- Listing copy: say "suspicious", not "broken". Credit Umbraco17Check and link it for code rules.

### Other distribution
- **GitHub Action:** tagged releases in `umbraco-package-check-action` (`v1`, `v1.0.0`). Document SHA pinning in the README. Optionally list on GitHub Marketplace.
- **Announce ("own it, announce it"):** a forum post or reply in thread 7933 when 0.1 ships; the overrides pitch after the M6 backtest (D5).

---

## 11. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| False `suspicious` flags upset package authors | Medium | High | "Suspicious", never "broken". Always show evidence. Authors can claim overrides. Backtest before CI blocks |
| Umbraco17Check adds inspection or a target flag | Medium | Medium | Ship 0.1 fast. Differentiate on exit codes, transitive, private feeds, overrides, multi-site |
| Deadline slips past 14 Dec | Medium | Medium | Cut list in section 4. Reserve the ID now |
| HQ fixes the Marketplace badges | Low-medium | Medium | Offer HQ the overrides data. CLI still wins for CI and patch picking |
| Marketplace API changes (not a contract) | Medium | Low | Optional, cached, degrades quietly, never causes exit 3 |
| NuGet rate limits on 20-site audits | Low | Medium | Global packages folder first, shared cache, concurrency 8, backoff |
| Private feeds (Engage, paid add-ons) | High | Low | `NuGet.config` via `NuGet.Protocol`; else `unknown` with reason |
| Malicious nupkg | Low | High | Metadata only, no loading, size and entry caps, path checks |
| Scope creep into code rules | High | Medium | D1. Packages only. Link to Umbraco17Check |
| Overrides repo gets stale or abused | Medium | Medium | `reviewBy` expiry, evidence links, two-maintainer merges, labelled output |
| Bus factor: Pascal is the only maintainer | High | Medium | Co-maintainer on the overrides repo (D5); offer the repo to the Community Package Team |
| Phase 3: `NuGet.Protocol` conflicts inside an Umbraco site | Unknown | Medium | Spike S7 before phase 3. Fallback: tier 1 and tier 3 only, no in-process lookups |
| Phase 3: production egress blocked (Cloud, firewalls) | High | Low | Outbound is opt-in; tiers 1 and 3 need no network |
| H10 API index needs work per major | Medium | Medium | Out of the MVP. Generate from core nupkgs |

---

## 12. Left to the builder

These are deliberately not decided. Pick, and note the choice in the repo.
- Test framework (xUnit, NUnit or MSTest) and snapshot library.
- Console rendering library (Spectre.Console or plain).
- Exact class names, folder layout inside each project, and whether Inputs/Sources/Inspect are separate assemblies or folders in one library.
- Evidence weighting maths, as long as the combining rules in 7.4 and the golden fixtures hold.
- `--msbuild` implementation details, or cutting it from 0.1 if time is short.
- SARIF rule ids and level mapping.
- CI provider for the tool's own repo (GitHub Actions expected).
- Phase 3 dashboard layout and UX, within the security rules and the Umbraco UI Library.
- Whether the overrides compile step is a .NET tool, a script or an action.

---

## 13. Questions for Pascal

**Builder check (2026-10-29):** a fresh Sonnet builder read this doc and raised 7 questions.
- **Answered already by the inputs:** 0.
- **Minor, decided as architect's calls:** 7. Each is now in the doc:
  1. Prerelease targets such as an 18 RC (section 6.1, target resolution).
  2. The exact evidence-to-verdict combining rule (section 7.4, truth table).
  3. `--transitive` with no lock file and no `--msbuild` (section 6.1).
  4. Sites whose projects are on different Umbraco versions (section 6.1).
  5. Whether an override beats conflicting evidence (section 7.4).
  6. What counts as a source failure for exit 3, including private feeds (section 7.3).
  7. Where the overrides seed and `umbraco-versions.json` live before M5 (section 7.8).
- **Pascal's:** none from the builder check. Nothing left blocks planning.

**One item the architect raised (not from the builder check):**
1. **Phase 3 NuGet ID.** IDs can never be renamed, and this doc picks `Umbraco.Community.PackageCheck.Backoffice` and reserves it with a 0.0.1-alpha (section 10).
   - **Default:** keep `Umbraco.Community.PackageCheck.Backoffice`. Tell the builder if you'd rather have another name before the alpha goes out.

## Resolved by Pascal (2026-10-30)

- Phase 3 NuGet ID: `Umbraco.Community.PackageCheck.Backoffice`. Reserve it early with a 0.0.1-alpha.
- From the plan review (2026-10-30):
  - **Astroboard AC:** golden tests prove the computed verdict with no overrides. With an override applied, the report shows the override's verdict and keeps `override.computedVerdict` (section 6.1 acceptance criteria, section 9).
  - **Prerelease targets:** compared against their release base for range checks; the report carries `prerelease: true` (section 6.1, target resolution).
  - **Several paths:** a path without an Umbraco project is warned about and skipped; exit 2 only when no path has a site; a missing path is exit 2 (section 6.1, path handling).

## Resolved by Pascal (2026-10-30, after security fixes)

- If 0.1 runs late, `explain` and the live community-override fetch may be cut (ship the bundled snapshot). Last resort only, after the 30 Nov and 4 Dec cut checks.
