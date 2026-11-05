# Spikes

The M0 spike record, in run order: S1, S1b, S2, S3, S4, S5, S6, then the M0 gate (Task 7). Each section was written by the task that ran it; dates are when it ran.

## S1: NuGet.Protocol range checks and the HTTP seam

Date: 2026-11-03. Code: `spikes/S1-nuget-protocol/` (standalone console app, net8.0, NuGet.Protocol 7.9.0 pinned, own empty Directory.Build.props and CPM switched off). Run with `dotnet run` in that folder. Target: Umbraco.Cms 17.7.2 exists on nuget.org (single net10.0 dependency group, so target TFM = net10.0).

What was run: registration for Umbraco.Community.Contentment through `PackageMetadataResource` on a `SourceRepository` built from `Settings.LoadDefaultSettings(<temp dir with NuGet.config>)`; group choice with `NuGetFrameworkUtility.GetNearest`; range checks with `VersionRange.Satisfies`; a custom `ResourceProvider` for `HttpHandlerResource` wrapping a counting `DelegatingHandler`; record/replay of canned responses from files; a NuGet.config mapping `Umbraco.*` to nuget.org with a dummy second feed at `http://127.0.0.1:9/v3/index.json`.

Results:
- Step 1 PASS: all 39 Contentment versions returned with per-TFM dependency groups (4.x have `any` groups with no Umbraco.Cms deps; 5.x net8.0; 6.x net9.0/net10.0; 7.x net10.0).
- Step 2: mechanics PASS, literal criterion FAIL on live data. 6.2.1 net10.0 group: `Umbraco.Cms.Core [17.0.0, 18.0.0)` allows 17.7.2 = True; 6.3.3 also True. The criterion premise (6.2.1 does not clear 17.7.2) does not hold on the live feed; it would hold only for a net9.0 target, where the group range is [16.0.0, 17.0.0). Range maths itself is correct (6.2.1 vs 18.0.0 = False; 13 stable versions, 6.0.0 to 6.3.4, clear 17.7.2).
- Step 3: mechanics PASS, literal criterion FAIL on live data. Pick = 6.3.4 (a 6.3.4 now exists and is listed), highest overall = 7.2.2. Algorithm gives 6.3.3 only if 6.3.4 is absent. Step 3 does not check that the pick clears the target, so Task 17 must check it.
- Step 4 PASS: 9 requests counted (service index, registrations, Cms pages); recorded 2 responses (4 files: .body/.url pairs): service index and Contentment registration. Replay served 2 canned responses, 0 misses, and the replayed version list (39 versions) equals the recorded list element by element (Program.cs compares the lists, not just counts).
- Step 5 PASS, but read it correctly: zero dummy requests is because the spike's own `PackageSourceMapping.GetConfiguredPackageSources` lookup chose only the `nuget` feed before any repository was built. It is not NuGet honouring the mapping. The control (querying the dummy directly registers 1 request, refused locally) shows the counter works and that NuGet.Protocol itself applies no mapping when repositories are built by hand. Task 12 must do the mapping lookup itself.

Findings that shape Task 12 / 23:
- The seam is a `ResourceProvider` for `HttpHandlerResource` returning `HttpHandlerResourceV3(clientHandler, ourMessageHandler)`. The replacement inner handler must set `AutomaticDecompression` (registration is gzip), or JSON parsing fails.
- NuGet caches the service index on disk (40 min) beneath the handler. A warm machine-wide cache hides that request from the seam, so the tool must set `NUGET_HTTP_CACHE_PATH` (or equivalent) to its own cache, and the offline test must use a cold one.
- The spike sets no User-Agent, so Task 12 must set the required one.
- Registration pages for Umbraco.Cms are paged (several requests); fetch cost is real, so avoid asking for Cms metadata when the TFM can be had elsewhere.

Decision: GO on NuGet.Protocol (IPackageMetadataSource sits on it; HTTP seam and offline guard live at the `HttpHandlerResource` provider; no plan change). The two literal pass values (6.2.1 not clearing 17.7.2; pick 6.3.3) are stale against the live feed, not a library failure. Recommendation: Pascal confirms; if the plan's Task 12/17 tests hard-code 6.2.1/6.3.3 as live expectations, use canned data (the recorded files in `spikes/S1-nuget-protocol/canned/` are a snapshot of 2026-11-03) or pick a package/target pair where the live outcome is stable. No plan changes triggered.

## S1b: credential providers when headless

- **Date:** 2026-11-03
- **What was run:** nothing. The spike needs a real private feed behind a NuGet credential provider (for example Azure Artifacts), and no such feed or credentials were available for this unattended build. Steps 1-3 could not be run.
- **Result:** not run, so the pass criterion (Step 1 returns versions; Steps 2 and 3 finish within 30 s with a 401-class failure and no prompt) is not met.
- **Decision: NO-GO. Pre-approved fallback taken** (plan Task 2b, "If it fails"; Pascal's instruction for this build, 2026-11-03).
- **Plan changes applied:**
  - Task 12: `FeedSettings` does not call `DefaultCredentialServiceUtility.SetupDefaultCredentialService`. Credentials come only from `NuGet.config` `packageSourceCredentials`, including `%ENV%` references.
  - A feed that answers 401 or 403 without usable `NuGet.config` credentials gives reason `private-feed`.
  - Task 33 README: private feeds need credentials in `NuGet.config` or environment variables; CI users pass them that way. The credential-provider path is unsupported in 0.1.
  - Recorded in "M0 decisions" below.
- **Open for Pascal:** to support credential providers later, rerun Steps 1-3 against a real Azure Artifacts feed. Record only the feed host, never the `NuGet.config` contents or a token.

## S2: inspect nupkgs from the zip stream

Date: 2026-11-03. Run by: backend-engineer (Task 3). Tool: `spikes/S2-nupkg-inspect` (net8.0, `dotnet run -c Release -- <folder>`; raw output in `spikes/S2-nupkg-inspect/output.json`).

### What was run

- Downloaded 12 nupkgs from the nuget.org flat container into a `mktemp -d` folder outside the repo (deleted afterwards). sha512 computed from the bytes (base64) and cross-checked with `openssl dgst -sha512 -binary | base64`: all 12 match.
- Each opened with `ZipArchive` in read mode over a `FileStream`; nothing extracted, no assembly loaded. Only the nuspec and `Microsoft.AspNetCore.StaticWebAssets.props` were read, through a byte-counting loop (50 MB per entry, 500 MB per nupkg, 20,000 entries) with the `..`/rooted-path check applied to every entry name. No package triggered `unsafe-archive`. XML parsing relied on the .NET defaults (DtdProcessing Prohibit, no resolver). The 250 MB nupkg cap and unsafe-entry handling were not exercised (no hostile or oversized package in the set); Task 19 hardens both.
- Substitutions: `Skybrud.Umbraco.Redirects 17.1.0-beta` does not exist; nearest is `17.1.0-beta003` (latest 17.1.0 prerelease; no 17.1.0 final; stable 17.0.5 and 18.0.0 exist on nuget.org). Contentment `6.2.1`, `6.3.3`, `7.2.1` are under the ID `Umbraco.Community.Contentment`; `4.7.0` and the "Our" ID are `Our.Umbraco.Community.Contentment` (its latest is `4.7.0`, a metapackage that depends on `.Core`). The latest of the new ID, `7.2.2`, was also downloaded. `Umbraco.Workflow.StaticAssets` resolved to `17.5.4` (the `[17.5.4, 18.0.0)` range floor).

### Path table

| Package | Version | umbraco-package.json | package.manifest | StaticWebAssets.props BasePath | JS under staticwebassets/ or App_Plugins/ | lib TFMs |
|---|---|---|---|---|---|---|
| astroboard | 1.1.3 | none | `App_Plugins/Astroboard/package.manifest` | no props | 3 | net8.0 |
| Crumpled.RobotsTxt | 3.1.1 | `staticwebassets/umbraco-package.json` | `staticwebassets/package.manifest` | `App_Plugins/Crumpled.RobotsTxt` | 0 | net8.0 |
| MediaWiz.Forums | 14.0.0 | none | none | `_content/MediaWiz.Forums` | 3 | net8.0 |
| Our.Umbraco.Community.Contentment | 4.7.0 | none | `content/App_Plugins/Contentment/package.manifest` | no props | 1 | none |
| Skybrud.Umbraco.Redirects | 17.1.0-beta003 | none | none | `App_Plugins/Skybrud.Umbraco.Redirects` | 25 | net10.0 |
| Umbraco.Community.Contentment | 6.2.1 | none | none | `/` | 94 | net10.0, net9.0 |
| Umbraco.Community.Contentment | 6.3.3 | none | none | `/` | 94 | net10.0, net9.0 |
| Umbraco.Community.Contentment | 7.2.1 | none | none | `/` | 124 | net10.0 |
| Umbraco.Community.Contentment | 7.2.2 | none | none | `/` | 124 | net10.0 |
| Umbraco.Community.SimpleDashboards | 17.2.0 | none | none | `/` | 2 | net10.0 |
| Umbraco.Workflow | 17.5.4 | none | none | no props | 0 | net10.0 |
| Umbraco.Workflow.StaticAssets | 17.5.4 | `staticwebassets/umbraco-package.json` | none | `App_Plugins/Umbraco.Workflow` | 147 | net10.0 |

### Nuspec dependencies (TFM groups, Umbraco ranges; used by Task 21 and S3)

| Package | Group TFM | Dependencies |
|---|---|---|
| astroboard 1.1.3 | net8.0 | Umbraco.Cms.Web.BackOffice 13.8.1; Umbraco.Cms.Web.Website 13.8.1 |
| Crumpled.RobotsTxt 3.1.1 | net8.0 | Umbraco.Cms.Web.Website 13.0.0 |
| MediaWiz.Forums 14.0.0 | net8.0 | MediaWiz.Client 14.0.0; Microsoft.Maui.Graphics 8.0.21; Microsoft.Maui.Graphics.Skia 8.0.21; SkiaSharp.NativeAssets.Linux.NoDependencies 2.88.8; Umbraco.Cms.Core 14.0.0; Umbraco.Cms.Examine.Lucene 14.0.0; Umbraco.Cms.Infrastructure 14.0.0; Umbraco.Cms.Web.Common 14.0.0; Umbraco.Cms.Web.Website 14.0.0 |
| Our.Umbraco.Community.Contentment 4.7.0 | (ungrouped) | Our.Umbraco.Community.Contentment.Core 4.7.0 |
| Skybrud.Umbraco.Redirects 17.1.0-beta003 | net10.0 | Skybrud.Essentials 1.1.69; Umbraco.Cms.Api.Management [17.0.0, 17.9.9); Umbraco.Cms.Core [17.0.0, 17.999.0); Umbraco.Cms.Web.Website [17.0.0, 17.999.0) |
| Umbraco.Community.Contentment 6.2.1 | net10.0 | Umbraco.Cms.Api.Management [17.0.0, 18.0.0); Umbraco.Cms.Core [17.0.0, 18.0.0); Umbraco.Cms.Web.Website [17.0.0, 18.0.0) |
| Umbraco.Community.Contentment 6.2.1 | net9.0 | Umbraco.Cms.Api.Management [16.0.0, 17.0.0); Umbraco.Cms.Core [16.0.0, 17.0.0); Umbraco.Cms.Web.Website [16.0.0, 17.0.0) |
| Umbraco.Community.Contentment 6.3.3 | net10.0 | Umbraco.Cms.Api.Management [17.0.0, 18.0.0); Umbraco.Cms.Core [17.0.0, 18.0.0); Umbraco.Cms.Web.Website [17.0.0, 18.0.0) |
| Umbraco.Community.Contentment 6.3.3 | net9.0 | Umbraco.Cms.Api.Management [16.0.0, 17.0.0); Umbraco.Cms.Core [16.0.0, 17.0.0); Umbraco.Cms.Web.Website [16.0.0, 17.0.0) |
| Umbraco.Community.Contentment 7.2.1 | net10.0 | Umbraco.Cms.Api.Management [18.0.0, 19.0.0); Umbraco.Cms.Core [18.0.0, 19.0.0); Umbraco.Cms.Web.Website [18.0.0, 19.0.0) |
| Umbraco.Community.Contentment 7.2.2 | net10.0 | Umbraco.Cms.Api.Management [18.0.0, 19.0.0); Umbraco.Cms.Core [18.0.0, 19.0.0); Umbraco.Cms.Web.Website [18.0.0, 19.0.0) |
| Umbraco.Community.SimpleDashboards 17.2.0 | net10.0 | Umbraco.Cms.Api.Common [17.0.0, 18.0.0); Umbraco.Cms.Api.Management [17.0.0, 18.0.0); Umbraco.Cms.Core [17.0.0, 18.0.0); Umbraco.Cms.Web.Website [17.0.0, 18.0.0); jcdcdev.Umbraco.Core [17.1.0, 18.0.0) |
| Umbraco.Workflow 17.5.4 | net10.0 | Umbraco.Workflow.Core [17.5.4, 18.0.0); Umbraco.Workflow.StaticAssets [17.5.4, 18.0.0); Umbraco.Workflow.Web [17.5.4, 18.0.0) |
| Umbraco.Workflow.StaticAssets 17.5.4 | net10.0 | Umbraco.Workflow.Core [17.5.4, 18.0.0) |

### sha512 (base64)

| Package | Version | sha512 |
|---|---|---|
| astroboard | 1.1.3 | `xwGuWX7OmL0pPVBTKGaRxu2J1Pe/5ErIwv0JdFlRviSBZztFGrIonq8pV/NIWox/RVqr6pcNOQpJD9HJnL0H1Q==` |
| Crumpled.RobotsTxt | 3.1.1 | `DKcbmhikuTLE8F6IWfERf6fgRohPqQ9i4hj5K/0uu06qqmdIYMm7iD6mjWivFG5z/qX5GcDF4vIArQ7VotqB3w==` |
| MediaWiz.Forums | 14.0.0 | `2G39/j5bWZf/AdS6kMnL44LKVmU2liPp0l8LXaLBnfi2gVhdW4S2Q4H5BCFJjsJKH1m5tKHIgX/xDAgn8zkmPQ==` |
| Our.Umbraco.Community.Contentment | 4.7.0 | `BCePjySozyYBVj0mdKgOVaeLoyBbYOXd+eEcoMThhaP06+vdFS5dVj3YVyyjMpPgVAQaAqfg0rhkl7k2DDpNrg==` |
| Skybrud.Umbraco.Redirects | 17.1.0-beta003 | `LIw02aDcZLfjHWbj99k12Hr8N2bIaNYhZT2IrNzrpYsidwCQixY9QZcOCviZJgwpG4U0id23sg9jZ89Fn3/sXQ==` |
| Umbraco.Community.Contentment | 6.2.1 | `5lC20z0RddWRHtLEN5K+BaSH7UQQE1atEzG9lq8wpcK76JF6PhiBAvF5z8PVzFLn552WSRO25YEeJAqw6934Rw==` |
| Umbraco.Community.Contentment | 6.3.3 | `lHqagJp+ZDiUkLtgwb5TVDirD78WELz7xTXdwDqAOSnB3q1JPbUNoAIVmxi3vYJxyGb+74I0g8vvG+bXGPK69A==` |
| Umbraco.Community.Contentment | 7.2.1 | `rKG4q2IPnM5zPEE0VBtq+kMXh30mqWTgwwQp8KhIB17LlBBOa/edddV1CAPsOJqPf5aK9teaeHITpsnCSqNgag==` |
| Umbraco.Community.Contentment | 7.2.2 | `xN1j+FTK6M+MjeDrbpisotCb4X5qbWRZjdBuBr4ix7wSSpCJSIdoWMesXbydPVnyhCu2ayeIsmgODxJCYnWNBQ==` |
| Umbraco.Community.SimpleDashboards | 17.2.0 | `/Hg72SdOeUS6ysmYiqRQUM1AGPZh4qh0rEjDSIoCKZqtL9z3y1yB09JXkgAXGk0Ro0q6w/WHbZNe+y2QjqxIgg==` |
| Umbraco.Workflow | 17.5.4 | `lTQrVQIaa+ClnhZP8bj2XBFJHl95ZiVcZP/X/ytLmCFAb9t8g7D22FnL+923fTs/vovaykOe9Wif3TSM7PEjcQ==` |
| Umbraco.Workflow.StaticAssets | 17.5.4 | `nqLK4HHDtSpk8oJORLsdAK+wAYFSxUgj5fLwvfC64+V/UiQZtmGs0bH7vbo2BiRGVW2l6KTqJtA++vEfO4KjaA==` |

### Result against the pass criterion

- astroboard 1.1.3: `App_Plugins/Astroboard/package.manifest`, no `umbraco-package.json`. PASS.
- Umbraco.Workflow 17.5.4: 10 entries, no `umbraco-package.json`, no props, no JS; direct dependency `Umbraco.Workflow.StaticAssets [17.5.4, 18.0.0)` holds `staticwebassets/umbraco-package.json` and BasePath `App_Plugins/Umbraco.Workflow`. PASS (depth 1 is enough for this package; the depth-3 walk stays).
- Crumpled.RobotsTxt 3.1.1: both `staticwebassets/umbraco-package.json` and `staticwebassets/package.manifest`; BasePath `App_Plugins/Crumpled.RobotsTxt`. PASS.
- All read from the stream, nothing written to disk. PASS.

### Findings the later tasks must absorb (not failures of the criterion)

1. **No manifest file is not a legacy/new signal.** Contentment 6.x and 7.x, SimpleDashboards 17.2.0 and Skybrud Redirects 17.1.0-beta003 ship backoffice JS under `staticwebassets/` (up to 124 files) but neither manifest file; no manifest file found; the registration mechanism can't be determined without loading the DLL, which we never do. H08 must treat "JS present, no manifest" as indeterminate (we never load the DLL), not as legacy and not as new.
2. **Legacy manifest path can sit under `content/`**: Our.Umbraco.Community.Contentment 4.7.0 has `content/App_Plugins/Contentment/package.manifest`. The `**/package.manifest` pattern catches it; match on suffix, not on a root-relative path.
3. **BasePath varies**: `App_Plugins/<id>` (Workflow.StaticAssets, Crumpled, Skybrud), `/` (Contentment 6/7, SimpleDashboards; JS under the `staticwebassets/App_Plugins/<id>/` form) and `_content/<id>` (MediaWiz.Forums, a Razor class library with no backoffice manifest). Parse the `BasePath` as-is, trim slashes, and treat `/` as "web root".
4. **Metapackage shape**: Our.Umbraco.Community.Contentment 4.7.0 has no `lib/`, and a flat (ungrouped) `<dependency>` list. `libTfms` can be empty and dependencies can be ungrouped; the parser handles both.
5. **Dependency ranges are per TFM group**: Contentment 6.x gives net10.0 -> Umbraco.Cms.* `[17.0.0, 18.0.0)` and net9.0 -> `[16.0.0, 17.0.0)`; so the target major picks the group (Task 19/21 must read the group, not the union). Skybrud's upper bound is `17.9.9` for Api.Management but `17.999.0` for the others, so ranges are not uniform within one group.
6. astroboard 1.1.3 (net8.0) depends on Umbraco.Cms.Web.BackOffice/Website 13.8.1 (exact-minimum, open upper bound), a useful H07 fixture for "old package, no upper bound".
7. MediaWiz.Forums 14.0.0 depends on Umbraco.Cms.* 14.0.0 with a non-Umbraco heavy dependency set; Umbraco.Cms.Core lower bound only.

### GO / NO-GO

**GO.** Every criterion holds from the zip stream alone; no plan change needed for Tasks 19 and 21 beyond the notes above (add synthetic-nupkg tests for findings 1 to 4 in Task 19).

Plan changes if no-go: none required. Suggested additions (not conditions): Task 19 pattern list stays `**/umbraco-package.json`, `**/package.manifest`, `build*/Microsoft.AspNetCore.StaticWebAssets.props`, `lib/*/`; H08 gets an explicit "no manifest file, JS present" indeterminate branch.

JS patterns recorded: `staticwebassets/*`, `staticwebassets/App_Plugins/*` and `content/App_Plugins/*`.

## S3: type-reference scan without loading

Date: 2026-11-03. Code: `spikes/S3-type-refs/` (standalone console app, net8.0, NuGet.Versioning 7.9.0 pinned, own empty Directory.Build.props and CPM switched off). Run with `dotnet run -- <temp dir outside the repo>`. Target: Umbraco.Cms 17.7.2 (net10.0 group).

What was run: nupkgs fetched from the nuget.org flat container into a `mktemp -d` folder (deleted afterwards); dependency data and version choice from the registration API. Each nupkg opened as a `ZipArchive` in read mode, entry names checked for `..`, rooted and drive-letter paths, entry count capped at 20,000, nupkg at 250 MB. Every `lib/**/*.dll` read through a byte-counting loop (50 MB per entry, 500 MB per nupkg) into a `MemoryStream`, then `PEReader` / `MetadataReader`. No entry was written to disk and no package assembly was loaded. H09 match: a `TypeReference` named `IPackageManifestReader` or `PackageManifest` in namespace `Umbraco.Cms.Infrastructure.Manifest` (nested type references take the outer type's namespace). Also recorded: refs whose namespace starts with `Umbraco.Cms.Api.Management` (for H11), and whether a type in the DLL implements `IPackageManifestReader`.

Depth: direct dependencies (depth 1) that are Umbraco packages, meaning they reach `Umbraco.Cms.*` within 3 hops. `Umbraco.Cms.*` itself is excluded. Dependency version = lowest listed version in the range (NuGet's lowest-applicable rule). Dependency group = net10.0, else the nearest netN.0.

### Step 1: SimpleDashboards

- `Umbraco.Community.SimpleDashboards 17.2.0` (`lib/net10.0/Umbraco.Community.SimpleDashboards.dll`): references `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader`; type `Umbraco.Community.SimpleDashboards.Core.PackageManifestReader` implements it. One Management API ref (`Umbraco.Cms.Api.Management.Filters.AppendEventMessagesAttribute`).
- `jcdcdev.Umbraco.Core` (range `[17.1.0, 18.0.0)`, resolved 17.1.0): no H09 refs, no Management API refs.

The reference is in SimpleDashboards itself, not in jcdcdev.Umbraco.Core as design H09 says ("SimpleDashboards uses `jcdcdev.Umbraco.Core` [verified]"). The pass criterion allows either, so Step 1 passes. The design text should name SimpleDashboards.

### Step 2: control set

No id needed a reserve swap. Versions used: as listed in the plan; Workflow.StaticAssets resolves to 17.5.4; the three unpinned ids took the latest listed stable whose `Umbraco.Cms*` range allows 17.7.2 (followed through the meta-package to its Core where there was no direct `Umbraco.Cms*` dependency).

| # | id | version | working | why | H09 flag |
|---|---|---|---|---|---|
| 1 | astroboard | 1.1.3 | no | 3 js, 1 `package.manifest`, no `umbraco-package.json`, no Umbraco deps | no |
| 2 | Umbraco.Workflow | 17.5.4 | yes | depth-1 dep Umbraco.Workflow.StaticAssets 17.5.4 ships `umbraco-package.json` | no |
| 3 | Umbraco.Workflow.StaticAssets | 17.5.4 | yes | own `staticwebassets/umbraco-package.json` | no |
| 4 | Crumpled.RobotsTxt | 3.1.1 | yes | own `staticwebassets/umbraco-package.json` | no |
| 5 | MediaWiz.Forums | 14.0.0 | no | 3 js under `staticwebassets/scripts/`, no `umbraco-package.json`, no Umbraco deps | no |
| 6 | Umbraco.Community.Contentment | 6.3.3 | no | 94 js under `staticwebassets/App_Plugins/Contentment/`, no `umbraco-package.json`, no Umbraco deps | **yes (own)** |
| 7 | Skybrud.Umbraco.Redirects | 17.0.5 | no | 16 js under `staticwebassets/` (incl. `EntryPoint.js`), no `umbraco-package.json`, no Umbraco deps | **yes (own)** |
| 8 | Our.Umbraco.GMaps | 17.3.1 | yes | own `staticwebassets/App_Plugins/Our.Umbraco.GMaps/umbraco-package.json` | no |
| 9 | Umbraco.Community.BlockPreview | 5.6.0 | yes | own `staticwebassets/App_Plugins/Umbraco.Community.BlockPreview/umbraco-package.json` | no |
| 10 | Umbraco.Forms | 17.6.2 | yes | depth-1 dep Umbraco.Forms.StaticAssets 17.6.2 ships `umbraco-package.json` | no |
| 11 | uSync | 17.6.1 | yes | meta-package has no client assets | yes (via deps uSync.Backoffice.Management.Client, uSync.History) |
| 12 | Umbraco.Commerce | 17.2.11 | yes | depth-1 dep Umbraco.Commerce.Cms.Web.StaticAssets 17.2.11 ships `umbraco-package.json` | no |

Management API refs (own + depth-1 deps, distinct type names): Workflow 12, Workflow.StaticAssets 8, uSync 9, Contentment 3, GMaps 2, BlockPreview 1, others 0.

Depth 2: the four "not working" rows (1, 5, 6, 7) have no Umbraco dependencies at all, so depth 2 changes no row. Rows already "working" cannot change.

**False positives: 2.** Contentment 6.3.3 and Skybrud.Umbraco.Redirects 17.0.5 are flagged by H09 and are "not working" under the plan's definition.

Every H09 hit in this run is a type that implements `IPackageManifestReader`, not just a stray reference: `ContentmentPackageManifestReader`, `Skybrud.Umbraco.Redirects.Manifests.RedirectsPackageManifestReader`, uSync's `SyncSectionManifestReader`, `uSyncManifestReader` and `uSyncHistoryManifestReader`, and SimpleDashboards' `PackageManifestReader`. That is how a 14+ package registers its backoffice manifest from C# in place of a `umbraco-package.json` file. Both "false positives" ship new-backoffice ES modules under `staticwebassets/` and declare `Umbraco.Cms` ranges that allow 17 (S1 showed this for Contentment 6.x). So they are almost certainly working packages that the file-based definition can't see [inferred: not run in an Umbraco 17 site]. SimpleDashboards, the H09 showcase, is in the same position: it has no `umbraco-package.json` either. The objective definition and H09 measure the same thing in two ways, and the definition only accepts one of them.

### Step 3: no-load check

27 lib DLLs scanned, 26 distinct simple names (assembly-definition name plus file name). After scanning, `AppDomain.CurrentDomain.GetAssemblies()` holds none of those names. Only one `AssemblyLoadContext` (Default) exists. 24 assemblies were loaded during the run, all `System.*` / `Microsoft.Win32.Primitives` runtime assemblies (HTTP, compression, metadata). Self-test: the same comparison finds `System.Reflection.Metadata` in the loaded set, so a match would have been detected. Source grep for `Assembly.Load`, `LoadFrom`, `LoadFile`, `MetadataLoadContext`, `ExtractTo*`: only the header comment that forbids them. Result: PASS, assembly set unchanged for package names.

### Result vs pass criterion

- H09 found in SimpleDashboards: PASS.
- Zero false positives: **FAIL (2: Contentment 6.3.3, Skybrud.Umbraco.Redirects 17.0.5)**, both artefacts of the definition (see above).
- No scanned assembly loaded: PASS.

**Decision: NO-GO as written** (found, with false positives). The plan's change: H09 weight becomes `medium` in Task 21; truth table rows 5 and 6 then need H07 or H16, so SimpleDashboards becomes `ok` `medium`. That changes a design acceptance criterion: take it to the Fri 6 Nov review with Pascal (Task 7). **Superseded:** ruling R23 made S3 GO with the tightened H09, weight high (see "M0 decisions" below, proposed).

For the controller and Pascal: the evidence points the other way. Both false positives implement `IPackageManifestReader`, which is the signal H09 is meant to detect, and nothing in the set suggests H09 fires on a package that is really broken (astroboard and MediaWiz.Forums, the genuinely legacy or front-end-only packages, are not flagged). If Pascal accepts "implements `IPackageManifestReader`" as an equal form of "ships `umbraco-package.json`" in the working definition, the false positives drop to zero and S3 is GO (H09 = high). That is a change to the spike definition, so it is a ruling for Pascal, not something this spike can decide. Suggested tightening either way: H09 should match an *implementation* of `IPackageManifestReader` (interface impl on a type definition), not any reference, which every hit here satisfies.

## S4: reading .slnx (go/no-go)

Date: 2026-11-03

**What ran:** `spikes/S4-slnx` console app (net8.0, standalone build settings). For each file it reads project paths with `Microsoft.VisualStudio.SolutionPersistence` 1.0.52 (`SolutionSerializers.GetSerializerByMoniker(...).OpenAsync(...).SolutionProjects[].FilePath`) and, for `.slnx`, with a short `XDocument` reader (`Descendants("Project")/@Path`). Run: `dotnet run -c Release -- <files>` from `spikes/S4-slnx`.

**Sources (no client repos read, per the controller ruling):**
- Public: `dotnet/aspnet-api-versioning` `asp.slnx` (175 lines, many `<Folder>`s), `devlooped/moq` `Moq.slnx`, `umbraco/Umbraco-CMS` `umbraco.sln` (10 solution folders). Fetched read-only via `gh api` into a temp dir, copied into `fixtures/`, temp dir deleted.
- Hand-written: `hand-nested.slnx` (flat `/src/web/sites/` folders, project two folders deep, root-level project, a `<File>`-only folder), `hand-nested-malformed.slnx` (Folder elements nested inside Folder elements, which is not the real format), `hand-folders.sln` (solution-folder type GUID 2150E333..., NestedProjects section).

**Results**

| File | Library | XDocument | Identical |
|---|---|---|---|
| public-moq.slnx | 13 | 13 | yes |
| public-aspnet-api-versioning.slnx | 69 | 69 | yes |
| hand-nested.slnx | 5 | 5 | yes |
| hand-nested-malformed.slnx | 2 | 4 | no (see below) |
| hand-folders.sln | 2 (folders skipped) | n/a | n/a |
| public-umbraco-cms.sln | 33 | n/a | grep of non-folder `Project(` lines also gives 33 |

**Observations**
- Real slnx nests folders by flat full-path names (`/src/web/sites/`), never by element nesting. On the malformed fixture the library silently drops projects inside nested Folder elements while the XDocument reader returns them. Not a pass-criterion issue (invalid input), but noted.
- `.sln` solution folders are skipped by the library with no GUID handling on our side.
- `.sln` may contain non-file "projects": Umbraco-CMS has 3 Web Site entries whose path is a URL (`http://localhost:3961`). Task 8 must drop them (see the Task 8 must list).
- Moq.slnx has a project with a `Type` attribute and a `.msbuildproj` extension; both readers return it, so the same extension filter applies.
- Path shape (verified on macOS, see fix round 1): `FilePath` is solution-relative (never rooted) and already uses `/` even for a `.sln` written with `\`. The library does not resolve `..` or join to the solution directory.

**Coverage limits (plainly):** real-world nested-folder coverage is one public file (aspnet-api-versioning `asp.slnx`) plus synthetic fixtures. Neither public .slnx is an Umbraco-site layout (a class library repo and a mocking library); the only Umbraco-shaped file is the Umbraco-CMS `.sln`. The GO rests on the library matching an independent reader, not on proof against Umbraco-site .slnx files. Windows path behaviour was not tested (macOS only). Fixture provenance and licences: `spikes/S4-slnx/fixtures/README.md`.

**Task 8 must:**
- Test `hand-nested-malformed.slnx` with an explicit outcome: for `.slnx`, cross-check the library's project count against the XDocument `Descendants("Project")` count and warn on mismatch, never return a silently short list.
- Resolve paths against the solution directory and handle `..`.
- Keep only `.csproj`, `.fsproj`, `.vbproj`; drop URL Web Site entries and other project types (`.msbuildproj`, `.shproj`, `.sqlproj`) deliberately.

**Licence / compatibility (1.0.52 nupkg/nuspec):** `license type="expression"` is `MIT`; `lib/net8.0` and `lib/net472` present; the net8.0 dependency group is empty (no transitive deps); repo microsoft/vs-solutionpersistence; authored by Microsoft. 1.0.52 is the latest on nuget.org at test time. Ran on the net8.0 target here.

**Pass criterion:** identical lists for both real .slnx files (met, plus the valid hand fixture), reads the .sln too (met), MIT (met), net8.0 (met). Deviation: the brief's two client .slnx/.sln files were replaced by public and synthetic files because client repos are off limits.

**Result: GO, use `Microsoft.VisualStudio.SolutionPersistence`** behind `SolutionFileReader`. Plan change: none. See the Task 8 must list. `fixtures/` can serve as Task 8 test input.

## S5: Marketplace listing rules for a DotnetTool

Date: 2026-11-03. Read-only GETs only. `validatePackage` and `validateJson` never called. Nothing posted.

Checked: Marketplace docs page, hosted JSON schema, Marketplace OpenAPI (api.marketplace.umbraco.com/openapi/v1.json), a sample of the catalogue via the packages API (pages 1-11, 550 of 1052 records, because the API caps pageSize at 50), plus Text searches, NuGet search API for tagged DotnetTool packages.

Findings
- [verified] Required NuGet tag is `umbraco-marketplace`. Tag only the installable package. Source: https://docs.umbraco.com/umbraco-dxp/marketplace/listing-your-package
- [verified] The package must depend on an Umbraco package (`Umbraco.Cms.*` or `Umbraco.Commerce.*`) or it is not listed (same docs page, read verbatim: a package without such a dependency "will not be listed").
- [verified] `umbraco-marketplace.json` is optional and is NOT read from the nupkg. It must be in the folder named by the NuGet projectUrl: for a GitHub projectUrl, the repo root of the default branch (or a deeper link if the projectUrl is one). Multi-package sites use `umbraco-marketplace-<package.id>.json`. Optional `umbraco-marketplace-readme.md` beside it overrides the NuGet readme. Same docs page.
- [verified] Schema URL: https://marketplace.umbraco.com/umbraco-marketplace-schema.json (fetched, HTTP 200). Top-level properties: $schema, AddOnPackagesRequiredForUmbracoCloud, AlternateCategory, AlternatePackageNames, AuthorDetails, Category, Description, DiscussionForumUrl, DocumentationUrl, IssueTrackerUrl, LicenseTypes, LicenseType, LookingForContributors, LookingForMaintainer, IsSubPackageOf, PackageType, PackagesByAuthor, RelatedPackages, Screenshots, Tags, Title, VersionDependencyMode, VersionSpecificPackageIds, VideoUrl. All optional.
- [verified] `PackageType` enum in the schema is only `Package` or `Integration`. There is no `Tool` or `DotnetTool` value to set in the JSON.
- [verified] The API filter documents `Package`, `Integration`, `Tool`, `Template` as package types, but live counts are Package 991, Integration 54, Template 7, Tool 0 (totalResults per PackageType query). The 7 "Template" matches all come back with packageType `Package`. All 550 sampled records are `Package`/`NuGet`.
- [verified] Categories (GET /api/v1.0/categories and docs): includes Developer Tools, Editor Tools, Analytics & Insights, Artificial Intelligence, Search, etc. Best fit for PackageCheck: `Developer Tools`.
- [verified] Fields worth setting for a tool: Category=`Developer Tools`, AuthorDetails (Name, Url, ImageUrl, SyncContributorsFromRepository), IssueTrackerUrl, DocumentationUrl, Screenshots (ImageUrl, Caption; a CLI report screenshot), LicenseTypes=["Free"], Tags, Description/Title only if overriding NuGet, PackageType=`Package`.
- [verified] NuGet search with packageType=DotnetTool and the tag finds exactly one tagged tool: `PackageScriptWriter.Cli` 1.2.1 (published 2026-05-01, tags include `umbraco-marketplace`, `dotnet-tool`, no package dependencies). It is absent from the 550 sampled records and from a Text search, as are `UForum-MCP` and `SplatDev.Umbraco.Tools.Packager` (other tagged non-library packages). That is one sample (PackageScriptWriter.Cli, published 2026-05-01), consistent with the dependency rule.
- [verified] Competitor Umbraco17Check is a DotnetTool and carries no `umbraco-marketplace` tag; it is not listed either.
- [unverified] Whether a DotnetTool that does reference an Umbraco package in its nuspec would list. No example exists. The plan should not rely on it.
- [unverified] Listing latency: docs say the NuGet scan runs daily at 04:00 UTC and known packages refresh every 2 hours. A forum claim of 24-48 h is community-written.
- [verified] Docs describe a single-package sync trigger: POST https://functions.marketplace.umbraco.com/api/InitiateSinglePackageSyncFunction (a write, so NOT called here; Pascal may use it after publishing).

Result vs pass criterion: the tag and the JSON fields are named with a [verified] source, so the literal criterion is met. But the verified evidence says tools are in practice not listed (Tool count 0, only tagged DotnetTool absent, Umbraco-dependency rule). A listing is not demonstrated.

S5 verdict: NO-GO: tools are not listed (0 Tool listings; docs say a package without an Umbraco.Cms.*/Umbraco.Commerce.* dependency will not be listed). Apply the brief's no-go branch.

Plan changes
- Task 33: drop `umbraco-marketplace.json`. Keep the `umbraco-marketplace` NuGet tag (controller ruling): it is harmless and lets HQ list the tool if they allow it. (If a listing is ever pursued, the JSON would go at the repo root, never in the nupkg; fields noted above.)
- M5 done-when becomes "on NuGet; Marketplace listing requested from HQ". Pascal must be told: this changes a milestone outcome.
- Task 36: check the listing at +24 h (and +48 h). If absent, Pascal asks HQ (the dependency rule makes absence likely). Do not add a fake Umbraco dependency just to be listed without Pascal deciding.
- Pascal action: after first stable publish, optionally trigger the single-package sync.

## S6: Competitor freshness (informational, no pass or fail)

Date: 2026-11-03. Read-only GETs only. `validatePackage` and `validateJson` never called. Nothing posted.

Checked 2026-11-03: Umbraco17Check on NuGet and GitHub, NuGet search for other DotnetTool upgrade checkers, forum thread 7933 JSON.

- [verified] Umbraco17Check: NuGet versions list only 0.1.0, published 2026-09-26 by OnLTS (an upgrade service). Repo https://github.com/mauri0686/umbraco17check, last push 2026-09-26, 0 stars, no GitHub releases. MIT. https://www.nuget.org/packages/Umbraco17Check
- [verified] Its README (nuget readme API) says it reads project files as XML, checks each package's declared Umbraco range on nuget.org, scans `App_Plugins` AngularJS extensions in the site, uSync/Deploy content-model files, and 20 text-searchable breaking changes. It "does not run MSBuild, query private feeds". Not inspecting nupkg contents. Promotes paid OnLTS upgrades. Sister tool Net10Check has versions 0.1.0 and 0.2.0.
- [verified] NuGet search for DotnetTool packages on "umbraco upgrade" returns only Umbraco17Check (120 downloads). No other community rival found. Searches on "umbraco v17", "compatibility", "nupkg", "analyzer" returned none.
- [verified] No HQ upgrade-readiness tool found: GitHub search in the umbraco org for "upgrade readiness" returned nothing. Absence in search, not proof.
- [verified] Forum thread https://forum.umbraco.com/t/packages-supporting-u17-when-they-do-not/7933: 18 posts, last 2026-08-10 (posts 10 to 18 on 7-10 Aug). Discussion is about Marketplace version badges being wrong, trust, verification, a rating system, and a suggestion to compile-test packages. Luuk (post 17) notes the Marketplace is a subset of NuGet with extra info and that "stale" marking was discussed but is risky. No mention of a tool, and no post since 2026-08-10.
- [verified] Nothing found that inspects nupkg contents (dll TFMs, staticwebassets, App_Plugins in package, umbraco-package.json). Rival checks rely on NuGet dependency metadata.

Result: no rival inspects nupkgs, so no README repositioning is forced. Still lead README with CI exit codes, private feeds, overrides and multi-site (design section 11), plus the nupkg-content angle as the differentiator. Note Umbraco17Check addresses the site scan side and its Umbraco 13 EOL (2026-12-14) framing; expect it to grow.

## M0 gate (Task 7)

Date: 2026-11-03. Code: `spikes/M0-golden/` (standalone console app, net8.0, NuGet.Protocol 7.9.0 pinned, own empty Directory.Build.props, CPM off). It wires the S1 seam (NuGet.Protocol with our handler in the `HttpHandlerResource` provider), the S2 path patterns and the S3 metadata scan to a hand-coded copy of the design 7.4 heuristics, combining rule and truth table, with rulings R1 (H06), R3 (H04 depth), R21 (nupkg patterns, TFM group) and R23 (H09, H08 suppression) applied. It prints computed verdicts. No override is applied (design 8, Pascal 2026-10-30).

Run: `dotnet run --project spikes/M0-golden -- --target 17.7.2`. Add `--verbose` to print every evidence line, `--record` to re-record.

**Reproducible by construction (R20).** Every metadata response (NuGet registration, the Umbraco.Cms nuspec that gives the target TFM, Marketplace detail) was recorded once on 2026-11-03 into `spikes/M0-golden/recorded/http/` (99 responses, 1.2 MB gzipped). A normal run replays them through a handler that has no network behind it, and a request the snapshot lacks stops the run with exit 3 (checked with `--target 17.7.1`). Nupkgs are not committed (licences vary, design 9): they download once into `$TMPDIR/umbraco-package-check-m0-nupkgs` and each must match its sha512 pin in `recorded/nupkgs.json` (30 nupkgs). Two warm runs and one with a cold nupkg cache printed byte-identical output.

**Safety.** Nupkgs are read as zip streams and nothing is extracted. DLL bytes go to `System.Reflection.Metadata` only. Unsafe entry names, the entry-count cap and the byte caps give `unsafe-archive`. The end-of-run check found none of the 17 scanned assembly names loaded in the process.

**Output** (replay run, re-run 2026-11-05 after the M0 review fixes R25-R34):

```
M0 gate: target Umbraco.Cms 17.7.2 (net10.0), computed verdicts, no overrides
Metadata: replayed from spikes/M0-golden/recorded (snapshot 2026-11-03); nupkgs pinned by sha512

== Gate (design 8 ACs, R19 amendment, G6) ==
astroboard 1.1.3 -> suspicious  [confidence medium; row 4; H03, H05, H08, H06]
Umbraco.Community.SimpleDashboards 17.2.0 -> ok  [confidence high; row 5; H09]
Umbraco.Workflow 17.5.4 -> ok  [confidence high; row 5; H07]
Umbraco.Community.Contentment 6.2.1 -> ok  [confidence high; row 5; H09]
Umbraco.Community.Contentment 5.2.1 -> upgrade 6.3.4 (highest overall 7.2.2, which does not support 17.7.2)  [confidence high; row 1; H02, H05, H06]  (G6, R19 fixture)

== Step 2 goldens and picks ==
Crumpled.RobotsTxt 3.1.1 -> ok  [confidence high; row 6; H03, H07, H06]
MediaWiz.Forums 14.0.0 -> ok  [confidence low; row 8; H06]
Umbraco.Community.Contentment 4.7.0 -> upgrade 6.3.4 (highest overall 7.2.2, which does not support 17.7.2)  [confidence high; row 1; H02, H05]
Umbraco.Community.Contentment 6.3.3 -> ok  [confidence high; row 5; H09]
Umbraco.Community.Contentment 7.2.1 -> no-release  [confidence high; row 1; H02, H09]
Our.Umbraco.Community.Contentment 4.7.0 -> no-release  [confidence high; row 1; H02, H05, H08]  (rename)
Skybrud.Umbraco.Redirects 13.0.9 -> upgrade 17.0.5 (highest overall 18.0.0, which does not support 17.7.2)  [confidence high; row 1; H02, H05, H06]
Skybrud.Umbraco.Redirects 13.0.9 --prerelease -> upgrade 17.1.0-beta003 (highest overall 18.0.0, which does not support 17.7.2)  [confidence high; row 1; H02, H05, H06]
astroboard 1.1.3 --no-inspect -> suspicious  [confidence medium; row 4; H03, H05, H06]
Plumber.Workflow 10.1.2 -> replaced by Umbraco.Workflow  [confidence high; row 9; H03, H14, H06]  (NuGet-deprecated pick)
Enterspeed.Source.UmbracoCms.V10 3.2.0 -> replaced by Enterspeed.Source.UmbracoCms  [confidence high; row 4; H03, H05, H14, H13, H08, H06]  (Marketplace-retired pick)
Skybrud.Umbraco.Redirects.Import 13.0.1 -> no-release (prerelease 17.0.0-beta001 supports it, offered only with --prerelease)  [confidence high; row 1; H02, H05, H06]  (H15 pick)
Skybrud.Umbraco.Redirects.Import 13.0.1 --prerelease -> upgrade 17.0.0-beta001  [confidence high; row 1; H02, H05, H06]  (H15 pick)
Umbraco.Community.Contentment 5.2.1 @ 18.3.0 -> upgrade 6.1.4 (highest overall 7.2.2)  [confidence high; row 1; H02, H05, H06]  (G6, two clearing majors)

No-load check: 28 DLLs scanned (17 names), loaded from packages: none; probe detects System.Reflection.Metadata: True
HTTP: 89 responses replayed, 0 live metadata calls; nupkgs used: 30
GATE: PASS (5 of 5 gate lines match)
```

Each line shows the verdict, then the confidence, the truth-table row that matched first and the evidence ids that carried it. The gate checks the first five lines against the design 8 ACs as amended by ruling R19, plus two extra conditions: SimpleDashboards must be `high` through H09, and Workflow's H07 must come from `Umbraco.Workflow.StaticAssets`.

**Gate lines**
- `astroboard 1.1.3`: `suspicious`, medium, row 4. Evidence includes the two lines the design AC names, H03 `open range >= 13.8.1` and H08 `legacy package.manifest only`, plus H05 (it depends on `Umbraco.Cms.Web.BackOffice`) and H06 (lib is net8.0 only).
- `Umbraco.Community.SimpleDashboards 17.2.0`: `ok`, high, row 5, through its own H09 (`Umbraco.Community.SimpleDashboards.Core.PackageManifestReader`).
- `Umbraco.Workflow 17.5.4`: `ok`, high, row 5. Workflow has no direct `Umbraco.Cms.*` dependency, so the range comes from the H04 walk (Workflow.Core, Workflow.Web, Umbraco.Licenses). H07 comes from `Umbraco.Workflow.StaticAssets 17.5.4` (and from `Umbraco.Licenses 17.0.5`).
- `Umbraco.Community.Contentment 6.2.1`: `ok`, high, row 5, through its own H09. This is the R19 amendment: its net10.0 group declares `[17.0.0, 18.0.0)`, so the design's "upgrade to 6.3.3" cannot be computed without bending the table.
- `Umbraco.Community.Contentment 5.2.1`: the G6 line. 5.2.1 is the newest 5.x; its only group (net8.0) declares `[13.2.0, 14.0.0)`. The pick is 6.3.4, the highest 6.x patch in the snapshot (R20: 6.3.4 shipped after the design named 6.3.3). The highest stable overall is 7.2.2, but 7.x declares `[18.0.0, 19.0.0)`, so no 7.x clears 17.7.2. At this target "not 7.x" is true but not tested. The last line of the output tests it properly at 18.3.0, where both 6.1.x (`[17.0.0, 19.0.0)`) and 7.x clear: the pick is 6.1.4, not 7.2.2.

**Step 2 lines**
- `Crumpled.RobotsTxt 3.1.1`: `ok`, high, row 6 (own H07). Its `package.manifest` is ignored because it also ships `umbraco-package.json`, so the manifests do not make it `suspicious` (design AC).
- `MediaWiz.Forums 14.0.0`: `ok`, low, row 8 (open range from 14, no high evidence either way; 3 JS files with no manifest file are indeterminate under R21). It is also deprecated on NuGet (Legacy) with no alternate, which is an info note only.
- `Umbraco.Community.Contentment 4.7.0`: `upgrade 6.3.4`. This id's 4.x versions are shims over `Our.Umbraco.Community.Contentment`, so the H04 walk finds the range at depth 3 (shim, Our, Our.Core, `Umbraco.Cms.Web.Website [13.0.0, 14.0.0)`).
- `Umbraco.Community.Contentment 6.3.3`: `ok`, high, row 5 (own H09).
- `Umbraco.Community.Contentment 7.2.1`: `no-release`, high. 7.x is the Umbraco 18 line and no newer version clears 17.7.2; a downgrade to 6.x is never offered (design 6.1: "a newer stable version").
- `Our.Umbraco.Community.Contentment 4.7.0` (the rename): computes `no-release`, high. The old id is not on the Marketplace (404) and is not deprecated on NuGet, so neither H13 nor H14 fires and the report does not point at the new id. The proposed expectation is `replaced by Umbraco.Community.Contentment` (high) once R30 adds the rename leg (M0 decisions, below); `golden-packages.json` keeps `computed` and `expected` apart until then.
- `Skybrud.Umbraco.Redirects 13.0.9`: `upgrade 17.0.5`; with `--prerelease`, `upgrade 17.1.0-beta003` (the lowest clearing major is 17, and with prereleases allowed its highest version is the beta).
- `astroboard 1.1.3 --no-inspect`: `suspicious`, medium, row 4, not row 9 as plan Task 7 labels it. H05 is metadata evidence, so it fires without inspection and row 4 matches first. The verdict is the same; the confidence is medium, not low.
- `Plumber.Workflow 10.1.2` (NuGet-deprecated pick): `replaced by Umbraco.Workflow`, from row 9.
- `Enterspeed.Source.UmbracoCms.V10 3.2.0` (Marketplace-retired pick): `replaced by Enterspeed.Source.UmbracoCms`, from row 4.
- `Skybrud.Umbraco.Redirects.Import 13.0.1` (H15 pick, R2): `no-release`, naming the prerelease `17.0.0-beta001`; with `--prerelease`, `upgrade 17.0.0-beta001`.
- `Umbraco.Community.Contentment 5.2.1 @ 18.3.0`: the G6 test with two clearing majors, `upgrade 6.1.4 (highest overall 7.2.2)`.

**Findings the later tasks must absorb**
1. Plan Task 17's test `Contentment621_Target1772_UpgradeTo633_Not7` expects `HighestCompatible "7.2.1"` at 17.7.2. On the data no 7.x clears 17.7.2, so `HighestCompatible` is null there. The gate prints "highest overall" as the highest stable version and says when it does not support the target. Task 17 should test the "not 7.x" rule at 18.x with the snapshot (6.1.4 over 7.2.2).
2. The table has two gaps where no row matches: an open range from 14 or later with a high negative (rows 3 and 4 need a bounded range or H03), and an open range from below 14 when the target is 13 or lower (rows 8 and 9 need a 14+ floor or a 14+ target). No golden reaches either. Ruling R28 closes them (widened rows 4 and 8, a missing floor counts as below 14, and an `unknown` fallback that Task 17 proves unreachable); see Truth table changes, below. Proposed, Pascal to confirm.
3. "H13 and H14 turn any row into `replaced` unless row 1 or 5-8 says the current version is fine" (design 7.4) can be read two ways for row 1. Ruling R27 takes the design's reading: row 1 is exempt only when it resolves to `upgrade` to a non-deprecated, non-retired version of the same id, so a row-1 `no-release` with a high H13/H14 becomes `replaced`. The M0 code applies it. Both picks land on rows 9 and 4, so neither reading changes a golden. Proposed, Pascal to confirm.
4. Dependency inspection only follows dependencies that have their own path to `Umbraco.Cms.*`. `Plumber.Backoffice 10.1.2` has an empty dependency group but ships `Content/App_Plugins/Plumber/...`, so it is skipped and Plumber.Workflow is reported as server-only. That does not change its verdict, because H08 counts the package's own files only (R23), but Task 21 should also inspect direct dependencies that ship client assets when it decides `IsServerOnly` (ruling R29, proposed; not built in the M0 spike). Server-only also never discards H07/H09 (R31, applied in the M0 code).
5. The Marketplace's "latest for target" lags NuGet: it reports Contentment 6.3.3 at 17.7.2 while 6.3.4 is listed. H12 stays info only.
6. Under R1, H06 fails for every net8.0-only package at 17 (astroboard, Crumpled.RobotsTxt, MediaWiz.Forums, Skybrud 13.x). It only changes a verdict in row 9.

## Decisions in place of the Fri 6 Nov review

Plan Task 7 Step 3 booked a results review with Pascal on Fri 6 Nov 2026. Pascal was away for this build and asked for the review to be replaced. The controller decided GO/NO-GO from the evidence above, on 2026-11-03. Every item below is **proposed, Pascal to confirm**. Tasks 17 onwards proceed on these values, and if Pascal changes one, the matching Task 17 or Task 24 test changes with it. Rationale, evidence and the cost of being wrong for each are in "M0 decisions" below; row and rule changes are in "Truth table changes" below.

| Item | Decision | Status |
|---|---|---|
| M0 | GO. The gate prints 5 of 5 lines as expected, with the Contentment AC amended by R19 | proposed, Pascal to confirm |
| S1 | GO on NuGet.Protocol | proposed, Pascal to confirm |
| S1b | NO-GO; fallback taken: credentials from NuGet.config `packageSourceCredentials` and environment variables only | proposed, Pascal to confirm |
| S2 | GO; patterns as in R21 | proposed, Pascal to confirm |
| S3 | GO with the tightened H09 (R23), weight high | proposed, Pascal to confirm |
| S4 | GO on Microsoft.VisualStudio.SolutionPersistence | proposed, Pascal to confirm |
| S5 | NO-GO (tools are not listed). M5 done-when becomes "on NuGet; Marketplace listing requested from HQ"; `umbraco-marketplace.json` dropped; `umbraco-marketplace` tag kept | proposed, Pascal to confirm |
| S6 | Informational; no rival inspects nupkgs | no decision needed |
| Truth table | No golden forced a row change. R28 widens rows 4 and 8 to close three gaps; R27, R31 and R32 settle the rules below the table. See "Truth table changes" below | proposed, Pascal to confirm |
| Step 2 goldens | As printed above and recorded in `tests/PackageCheck.Golden.Tests/golden-packages.json` (`expected`, `status`) | proposed, Pascal to confirm |
| Picks | NuGet-deprecated: Plumber.Workflow 10.1.2. Marketplace-retired: Enterspeed.Source.UmbracoCms.V10 3.2.0. H15: Skybrud.Umbraco.Redirects.Import 13.0.1. Skybrud current version: 13.0.9 | proposed, Pascal to confirm |
| Open questions | H13/H14 on row 1 (proposed: R27); the table gaps (proposed: R28); whether the rename should point at the new id (proposed: R30); whether Contentment 6.1.x's `[17.0.0, 19.0.0)` range should be trusted at 18 (R25 flag) | for Pascal |

## M0 decisions (proposed, Pascal to confirm)

One entry per decision, newest last. Each entry says what was decided, the evidence, what it costs if it is wrong, and its status. Pascal was away for the M0 build (from 2026-11-03), so anything that changes the design or a milestone is **proposed, Pascal to confirm**. The design (`docs/design.md`) stays verbatim; corrections to it are recorded here. (This section was `docs/decisions.md` until 2026-11-05; it moved here because `docs/` holds only design, plan and spikes.)

### 2026-11-03: S1b, credentials from NuGet.config and environment variables only

- **Decided:** 0.1 does not call NuGet's credential-provider setup (`DefaultCredentialServiceUtility.SetupDefaultCredentialService`). Credentials come only from `NuGet.config` `packageSourceCredentials`, including `%ENV%` references. A feed that answers 401 or 403 without usable credentials gives reason `private-feed`. The README says so, and CI users pass credentials the same way.
- **Evidence:** spike S1b could not run. It needs a real private feed behind a credential provider (for example Azure Artifacts), and none was available to this unattended build. Plan Task 2b pre-approves this fallback for a failed or unrun S1b. See S1b.
- **Cost if wrong:** teams whose private feed only works through a credential provider see `unknown` / `private-feed` for those packages, until they put credentials in `NuGet.config` or environment variables. Supporting providers later means re-running S1b Steps 1 to 3 against a real feed (record only the feed host) and adding the provider call in Task 12's `FeedSettings`.
- **Status:** proposed, Pascal to confirm.

### 2026-11-03: S3 GO, H09 tightened to "implements IPackageManifestReader", weight high

- **Decided:** H09 fires when a type defined in the target-TFM `lib/` DLL of the package, or of one of its Umbraco dependencies, implements the interface type reference `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader` whose resolution scope is the assembly reference `Umbraco.Infrastructure` (ruling R23). The design's `PackageManifest` leg is dropped. Weight: high positive. Only the lib group the target TFM selects is scanned. Only evidence in the package itself (its own H07 or H09) suppresses its own H08; dependency-only H07 or H09 next to the package's own H08 gives row 2 (`unknown`, conflicting evidence).
- **Evidence:** S3 scanned SimpleDashboards and a 12-package control set without loading any assembly. Every H09 hit was a type implementing `IPackageManifestReader`: SimpleDashboards' own `PackageManifestReader`, Contentment's `ContentmentPackageManifestReader`, Skybrud's `RedirectsPackageManifestReader` and three uSync readers. That interface is Umbraco's v14+ way to register a backoffice manifest from C# instead of shipping `umbraco-package.json`. The plan's working definition ("ships `umbraco-package.json`") graded SimpleDashboards itself, the design's H09 showcase, as not working, so two "false positives" (Contentment 6.3.3, Skybrud 17.0.5) were artefacts of that definition. The `PackageManifest` name also exists as the v10-13 model in `Umbraco.Cms.Core.Manifest`, so matching it would invite real false positives. The M0 gate run shows the tightened rule giving the design's SimpleDashboards AC (`ok`, high, via H09).
- **Circularity, stated plainly:** "zero false positives" holds by construction, not by measurement. Once the definition of a working package accepts "implements `IPackageManifestReader`", H09 cannot disagree with it. No package in the set was run in an Umbraco 17 site. What the evidence does show is that H09 did not fire on the genuinely legacy or front-end-only packages (astroboard, MediaWiz.Forums). A reader that is implemented but never registered would still pass; that limitation is documented, not guarded, in 0.1.
- **Cost if wrong:** H09 drops to medium. Truth-table rows 5 and 6 then need H07 or H16, so SimpleDashboards becomes `ok` medium (a design AC change), Contentment 6.x becomes `ok` medium, and Task 21's weight and one golden change.
- **Status:** proposed, Pascal to confirm.

### 2026-11-03: S5 NO-GO, Marketplace listing requested from HQ

- **Decided:** M5's done-when changes from "on NuGet and listed on the Marketplace" to "on NuGet; Marketplace listing requested from HQ". Task 33 drops `umbraco-marketplace.json`. The `umbraco-marketplace` NuGet tag stays, because it is harmless and lets HQ list the tool if they decide to allow tools. Task 36 checks for a listing at +24 h and +48 h and, if absent, Pascal asks HQ. No fake Umbraco dependency is added to get listed.
- **Evidence:** the Marketplace has 0 `Tool` listings (991 Package, 54 Integration, 7 Template). Its docs say a package without an `Umbraco.Cms.*` or `Umbraco.Commerce.*` dependency "will not be listed". The JSON schema's `PackageType` only allows `Package` or `Integration`. The one tagged DotnetTool found on NuGet (`PackageScriptWriter.Cli`) is not listed. See S5.
- **Cost if wrong:** if tools can be listed after all, the JSON file is re-added at the repo root (never in the nupkg) with the fields recorded in S5. That is one file.
- **Status:** proposed, Pascal to confirm. This changes a milestone outcome.

### 2026-11-03: Contentment AC substitution (ruling R19)

- **Decided:** the design AC "Given Contentment 6.2.1 and `--target 17.7.2`, then the verdict is `upgrade` to 6.3.3, not 7.x" is recorded as `Umbraco.Community.Contentment 6.2.1 -> ok` (high, row 5, own H09). The AC's intent, G6 (lowest clearing major, highest patch in it, not 7.x), is carried by `Umbraco.Community.Contentment 5.2.1 -> upgrade 6.3.4` at 17.7.2, and tested with two clearing majors by `Umbraco.Community.Contentment 5.2.1 @ 18.3.0 -> upgrade 6.1.4 (highest overall 7.2.2)`. Ruling R19 named Contentment 4.7.0 for G6; the Task 7 brief asked for the newest 5.x, which is a direct dependency on `Umbraco.Cms.*` and so tests G6 without the depth-3 walk. 4.7.0 is kept as its own golden (`upgrade 6.3.4`).
- **Evidence:** 6.2.1's nuspec has a net10.0 group with `Umbraco.Cms.Core`, `Umbraco.Cms.Web.Website` and `Umbraco.Cms.Api.Management` at `[17.0.0, 18.0.0)`, which allows 17.7.2. The AC would only hold for a net9.0 target, whose group is `[16.0.0, 17.0.0)`. Under the design's own table that is row 7 on metadata and row 5 with H09; no row change makes it `upgrade` without breaking the other goldens. 7.x declares `[18.0.0, 19.0.0)`, so at 17.7.2 no 7.x clears and "not 7.x" is not exercised; at 18.3.0, 6.1.x (`[17.0.0, 19.0.0)`) and 7.x both clear, and the gate picks 6.1.4. See S1 and the M0 gate.
- **Cost if wrong:** one golden expectation and its Task 24 test. If Pascal wants a literal "upgrade" line at 17.7.2 from 6.x, there is none on today's feed.
- **Status:** proposed, Pascal to confirm. This changes one of the four design ACs.

### 2026-11-03: goldens run on recorded snapshots, never live feeds (ruling R20)

- **Decided:** every golden and range test runs on HTTP responses recorded on 2026-11-03, replayed through a handler with no network behind it. Nupkgs are pinned by sha512 and downloaded by hash into a cache outside the repo. Upgrade picks reflect the snapshot, so the design's "6.3.3" reads as "the highest 6.x patch at snapshot time", which is 6.3.4. The M0 gate keeps its own snapshot in `spikes/M0-golden/recorded/`; Task 24 records its own the same way, and a weekly live job re-records and shows the diff (design 9).
- **Evidence:** the live feed moved after the design was written (Contentment 6.3.4, Skybrud 17.0.x stable, Contentment 7.x for Umbraco 18). Three M0 runs (two warm, one with a cold nupkg cache) printed byte-identical output. A request outside the snapshot fails with exit 3 instead of reaching the network.
- **Recorded Marketplace bodies (ruling R33):** only the fields the engine reads are kept (ids, retirement, alternate, HQ flag, version fields). `readMeContent`, `description` and other author-written free text are stripped before commit, by the recorder and, for the 2026-11-03 snapshot, by a one-off pass over the 10 recorded bodies (124 KB down to 6 KB). The verdicts did not change.
- **Cost if wrong:** if Pascal wants live expectations, every golden becomes flaky as packages ship. If he wants literally 6.3.3, the snapshot must exclude 6.3.4, which means editing recorded data by hand.
- **Status:** proposed, Pascal to confirm.

### 2026-11-03: corrections to the design text (ruling R24)

`docs/design.md` stays verbatim. These statements in it are wrong on the evidence and are corrected here.

- **H09 (design 7.4):** "Assembly type-references `IPackageManifestReader` or `PackageManifest` (in `Umbraco.Cms.Infrastructure.Manifest`)". Correct reading: a type defined in the package implements `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader` (from the `Umbraco.Infrastructure` assembly). `PackageManifest` is dropped; a type of that name in `Umbraco.Cms.Core.Manifest` is the v10-13 model. See the S3 decision above.
- **H09 (design 7.4):** "SimpleDashboards uses `jcdcdev.Umbraco.Core` [verified]". The H09 hit is in `Umbraco.Community.SimpleDashboards` itself (`Umbraco.Community.SimpleDashboards.Core.PackageManifestReader`); `jcdcdev.Umbraco.Core 17.1.0` has none (S3 Step 1).
- **Contentment (design 6.1 AC, 8, 9):** "Contentment 6.2.1 ... `upgrade` to 6.3.3" at 17.7.2. 6.2.1 allows 17.7.2 (`[17.0.0, 18.0.0)` in its net10.0 group). See the R19 decision above.
- **Skybrud (design 6.1 AC, 9):** "Skybrud Redirects with only a `17.1.0-beta` supporting the target". `Skybrud.Umbraco.Redirects 17.0.5` is stable and allows 17.7.2 (`[17.0.0, 17.999.0)`), and the beta is `17.1.0-beta003`. The prerelease-only case is proved with a different pair (below).
- **Status:** recorded corrections; the substance of each is proposed, Pascal to confirm.

### 2026-11-03: M0 gate GO

- **Decided:** M0 is done. `dotnet run --project spikes/M0-golden -- --target 17.7.2` prints computed verdicts with no overrides, and the gate check passes 5 of 5: astroboard `suspicious`, SimpleDashboards `ok` (high, H09), Workflow `ok` (H07 in `Umbraco.Workflow.StaticAssets`), Contentment 6.2.1 `ok` (R19), Contentment 5.2.1 `upgrade 6.3.4` with 7.2.2 as the highest overall (G6). No golden forced a truth-table row change; ruling R28 widens rows 4 and 8 to close three gaps no golden reaches (see "Truth table changes" below, proposed).
- **Evidence:** the output in the M0 gate section above. No golden needed the table bent.
- **Cost if wrong:** Tasks 17 onwards start on proposed expectations; a change by Pascal changes the matching Task 17 or Task 24 test.
- **Status:** proposed, Pascal to confirm (the controller decided GO from evidence in place of the 6 Nov review).

### 2026-11-03: proposed golden verdicts for the Step 2 packages

All at target 17.7.2, computed, no overrides, from the recorded snapshot. Recorded as `expected` in `tests/PackageCheck.Golden.Tests/golden-packages.json`. The ones the brief asked to justify individually:

- **MediaWiz.Forums 14.0.0 -> `ok`, low (row 8).**
  - Evidence: one net8.0 group with `Umbraco.Cms.*` at `>= 14.0.0` (open, floor at 14, so no H03). No `umbraco-package.json`, no `package.manifest`, no H09. 3 JS files under `staticwebassets/` with neither manifest file, which is indeterminate under R21, so H08 does not fire. H06 fails (lib is net8.0 only) but only matters in row 9. It is also deprecated on NuGet (Legacy) with no alternate: an info note, because H14 needs an alternate to name.
  - Cost if wrong: one golden expectation. If Pascal knows it fails on 17, that is an override (H16), not a table change.
  - Status: proposed, Pascal to confirm.
- **Our.Umbraco.Community.Contentment 4.7.0 (the rename) -> expected `replaced by Umbraco.Community.Contentment`, high; the M0 engine computes `no-release`, high (row 1).** The expectation depends on R30 (rename leg) and R27 (row-1 `no-release` with high H13 becomes `replaced`); see those entries. Until R30 is built the computed and expected values differ, which `golden-packages.json` records in separate `computed` and `expected` fields.
  - Evidence: the metapackage depends on `Our.Umbraco.Community.Contentment.Core [4.7.0, )`, whose net8.0 group declares `Umbraco.Cms.Web.Website` and `Umbraco.Cms.Web.BackOffice` at `[13.0.0, 14.0.0)`. No newer version exists under the old id. The old id returns 404 from the Marketplace and is not deprecated on NuGet, so neither H13 nor H14 fires and the verdict does not name `Umbraco.Community.Contentment`. The Marketplace entry for the new id does list the old id under `versionSpecificPackageIds` (Umbraco 8 to 12), so a reverse lookup could find the rename. 0.1 has no such lookup.
  - Cost if wrong: if Pascal prefers `no-release` for a rename, R30 is dropped and this golden reverts to the computed value.
  - Status: proposed, Pascal to confirm.
- **NuGet-deprecated pick: Plumber.Workflow 10.1.2 -> `replaced by Umbraco.Workflow`, high (row 9).**
  - Why this one: it is deprecated on NuGet (reasons Other, Legacy; message "Use the latest Umbraco.Workflow 10.x release.") with alternate `Umbraco.Workflow`, the package already in the goldens. Sites that installed Workflow under its earlier name have exactly this upgrade path. Its range `Umbraco.Cms.Web.Website >= 10.2.1` is open from below 14, so it lands on row 9 (H03, and H06 fails: net6.0 only). Row 9 is outside rows 1 and 5-8, so H14 gives `replaced` under either reading of the H13/H14 rule (see the next entry). Picked from a scan of the top 1,000 "umbraco" packages on NuGet for deprecation metadata.
  - Cost if wrong: one golden and its hash entry.
  - Status: proposed, Pascal to confirm.
- **Marketplace-retired pick: Enterspeed.Source.UmbracoCms.V10 3.2.0 -> `replaced by Enterspeed.Source.UmbracoCms`, high (row 4).**
  - Why this one: the Marketplace lists 9 retired packages (`IncludeRetired=true&RetiredOnly=true`). This one has an alternate that exists in the catalogue, a real retirement reason ("no longer maintained ... use our new package which works across multiple Umbraco versions") and an open range `>= 10.1.0` that lands on row 4 (H03 with H05 for `Umbraco.Cms.Web.BackOffice` and H08 for its own legacy `package.manifest`). Row 4 gives `replaced` under either reading of the H13/H14 rule. Every one of the 9 retired packages is also deprecated on NuGet, so H13 cannot be isolated from H14 with live data; both fire here and name the same alternate.
  - Cost if wrong: one golden and its hash entry.
  - Status: proposed, Pascal to confirm.
- **H15 pick (ruling R2): Skybrud.Umbraco.Redirects.Import 13.0.1 -> `no-release` naming prerelease `17.0.0-beta001`; with `--prerelease`, `upgrade 17.0.0-beta001`.**
  - Why this one: 13.0.1 is the newest stable and declares `[13.0.0, 13.999.0)`. The only 17.x is `17.0.0-beta001`, which declares `[17.0.0, 17.999.0)`, so only a prerelease clears 17.7.2. It is the same author and shape as the design's Skybrud AC, which no longer holds for Skybrud Redirects itself. Found by scanning the top 1,000 "umbraco" packages for a major with prereleases only. uSync 18.3.0 at 19.0.0-beta1 was tried first and rejected: it reaches `Umbraco.Cms.*` through `uSync.Core`'s open range `>= 18.1.0`, so it computes `ok`.
  - Cost if wrong: two goldens. When a stable 17.x ships, re-recording turns this into `upgrade`, which is why it runs on the snapshot.
  - Status: proposed, Pascal to confirm.
- **Skybrud.Umbraco.Redirects 13.0.9 -> `upgrade 17.0.5`; with `--prerelease`, `upgrade 17.1.0-beta003`** (R2: recorded as computed). 13.0.9 is the newest 13.x, the version a v13 site upgrading to 17 would hold. The backtest list (design 9) shows the upgrade guide chose 17.0.5. Status: proposed, Pascal to confirm.
- **astroboard 1.1.3 `--no-inspect` -> `suspicious`, medium (row 4, not row 9).** Plan Task 7 labels this golden "truth table row 9". H05 (depends on `Umbraco.Cms.Web.BackOffice`) is metadata evidence, so it still fires with `--no-inspect`, and row 4 matches before row 9. Without H05 it would be row 9, `suspicious` low (H03 plus H06 failing under R1). Ruling R26 settles how this sits next to plan Task 21: its `NoInspect_AstroboardShape_IsRow9` is a synthetic shape without the BackOffice dependency, and the plan's expectation for it (`Ok Low`) conflicts with R1. Under R26 that test expects `Suspicious Low` (H03 plus H06 failing from the nuspec groups), and a twin with a net10.0 group expects `Ok Low`. So Task 21's test changes; this golden stays row 4, `suspicious` medium. Status: proposed, Pascal to confirm.
- **The rest** (Crumpled.RobotsTxt 3.1.1 `ok` high row 6; Contentment 4.7.0 `upgrade 6.3.4`; Contentment 6.3.3 `ok` high row 5; Contentment 7.2.1 `no-release` high) are listed with their evidence in the M0 gate section above. Status: proposed, Pascal to confirm.

### 2026-11-03: H13/H14 and row 1 (ruling R27)

- **Decided:** H13 (Marketplace retired or renamed) and H14 (NuGet deprecation with an alternate) turn the result into `replaced` (high), naming the alternate, except on rows 5 to 8 and on a row 1 that resolves to `upgrade` to a non-deprecated, non-retired version of the same id. There they add a note only. A row-1 `no-release` with a high H13/H14 becomes `replaced` and keeps its range and H15 evidence lines. H13/H14 never override `unknown` / `unsafe-archive` (R32). This supersedes plan Task 17's "unless the row is 1 or 5-8".
- **Evidence:** design 7.4 says "unless row 1 or 5-8 says the current version is fine", and a row-1 `no-release` never says that. Most deprecated packages are old and land on row 1, so the plan's reading would almost never meet the design AC "a package NuGet deprecated with an alternate ... is `replaced`". `Umbraco.Community.Forms.Mailcoach 13.0.0` is a package that tells the readings apart: deprecated on NuGet and retired on the Marketplace, alternate `Umbraco.Community.Forms.Workflows`, range through `Umbraco.Forms.Core 13.5.0` (`Umbraco.Cms.Web.BackOffice [13.0.0, 14.0.0)`), so row 1 at 17.7.2 (checked against the live registration on 2026-11-03; not in the M0 snapshot). It is `replaced` under R27 and `no-release` under the plan's reading. The M0 engine applies R27; neither pick changes (rows 9 and 4).
- **Cost if wrong:** one rule in Task 17's `TruthTable` and its unit tests. The rename golden's expectation depends on it (with R30).
- **Status:** proposed, Pascal to confirm.

### 2026-11-03: "highest overall" in an upgrade line

- **Decided:** the M0 gate prints "highest overall" as the highest stable version newer than the current one, and says when it does not support the target. Plan Task 17's `HighestCompatible` (the highest version that clears on metadata) is printed separately as "highest compatible" when it differs.
- **Evidence:** at 17.7.2, Contentment 7.x does not clear, so `HighestCompatible` is empty for the G6 line, while the brief and design 6.1 expect 7.x to be named. Plan Task 17's test `Contentment621_Target1772_UpgradeTo633_Not7` expected `HighestCompatible "7.2.1"` at 17.7.2, which is false on the data; ruling R25 replaces it (next entry).
- **Cost if wrong:** report wording and one Task 17 test expectation.
- **Status:** proposed, Pascal to confirm.

### 2026-11-03: Task 17 G6 tests replaced (ruling R25)

- **Decided:** plan Task 17's `Contentment621_Target1772_UpgradeTo633_Not7` is replaced by two tests on the recorded snapshot: `Contentment521_Target1772_UpgradeTo634` (Recommended 6.3.4, HighestCompatible null, highest stable 7.2.2 flagged "does not support") and `Contentment521_Target1830_PicksLowestClearingMajor` (Recommended 6.1.4, HighestCompatible 7.2.2). The M0 gate asserts the first as the R19 fixture (confidence high, `HighestCompatible` empty, highest stable 7.2.2 and not clearing).
- **Evidence:** the M0 gate lines `Umbraco.Community.Contentment 5.2.1 -> upgrade 6.3.4 (highest overall 7.2.2, which does not support 17.7.2)` and `Umbraco.Community.Contentment 5.2.1 @ 18.3.0 -> upgrade 6.1.4 (highest overall 7.2.2)`.
- **Flag for Pascal:** Contentment 6.1.0 to 6.1.4 declare `[17.0.0, 19.0.0)` in their net10.0 group, while 6.2.0 onwards narrowed it to `[17.0.0, 18.0.0)`. That looks like an over-broad range the author later corrected. So the 18.3.0 pick of 6.1.4 is the correct output of the rule but is probably a false upgrade in practice: the test enshrines what the rule does, not that 6.1.4 works on 18.
- **Cost if wrong:** two Task 17 tests. If Pascal judges the 6.1.x range untrustworthy, that is an override (H16) for Contentment 6.1.x at 18, not a selection-rule change.
- **Status:** proposed, Pascal to confirm.

### 2026-11-03: inspect asset-only direct dependencies (ruling R29)

- **Decided:** Task 21 also inspects direct, non-platform dependencies that have no Umbraco path but ship client assets (`App_Plugins/`, `content/App_Plugins/`, `staticwebassets/`), within the same caps. Their assets count when deciding server-only, and their legacy `package.manifest` counts as the package's own H08.
- **Evidence:** `Plumber.Backoffice 10.1.2` has an empty netstandard2.0 dependency group, so it has no path to `Umbraco.Cms.*` and the M0 engine never inspects it, yet it ships `Content/App_Plugins/Plumber/...` (the v8-13 backoffice). The M0 engine therefore reports `Plumber.Workflow 10.1.2` as server-only. Its verdict is unaffected (row 9, then `replaced` by H14). Under R29 its H08 would fire, moving it to row 4, still `replaced`.
- **Cost if wrong:** more nupkg downloads per package, which the caps bound. Without it, split "UI" packages hide their legacy manifest, which is the H08 case the tool exists for.
- **Status:** proposed, Pascal to confirm. Not built in the M0 spike (Task 21).

### 2026-11-03: rename leg of H13 (ruling R30)

- **Decided:** an id listed in another Marketplace entry's `versionSpecificPackageIds` counts as renamed, and the alternate is that entry's id for the target major. Task 16 builds it if the Marketplace API exposes a reverse lookup; otherwise the seed overrides carry the known renames. With R27, the `Our.Umbraco.Community.Contentment 4.7.0` golden becomes `replaced by Umbraco.Community.Contentment` (high).
- **Evidence:** `GET /api/v1.0/packages/Our.Umbraco.Community.Contentment` returns 404. `GET /api/v1.0/packages/Umbraco.Community.Contentment` lists the old id under `versionSpecificPackageIds` for Umbraco 8 to 12, and `packageIdForProvidedUmbracoVersion` is the new id at 17.7.2. No lookup from the old id to the new one was found in the OpenAPI document (parameters for `/packages` include `Text` but no id-in-`versionSpecificPackageIds` filter), so the seed-override route is the likely one.
- **Cost if wrong:** the rename golden reverts to `no-release`, and users of renamed packages get no pointer to the new id.
- **Status:** proposed, Pascal to confirm. Not built in the M0 spike: it computes `no-release`.

## Truth table changes (proposed, Pascal to confirm)

Design 7.4 says that if a golden fixture and the truth table disagree during M0, the builder fixes the table, not the fixture, and records the changed row, the fixture that forced it and the reason here. Task 17 builds the table from design 7.4 plus this section. (This section was `docs/truth-table-changes.md` until 2026-11-05.)

Every entry is **proposed, Pascal to confirm** (Pascal was away for the 6 Nov review; see "M0 decisions" above).

### Row changes

No golden forced a row change: every golden matched a design 7.4 row as written (output in the M0 gate section above). The M0 review found three places where no row matches at all, and ruling R28 closes them. Rows 1 to 3, 5 to 7, 9 and 10 are unchanged.

| Row | Design 7.4 | Changed to (R28) | What forced it | Reason |
|---|---|---|---|---|
| 4 | allows, open (H03); other high negative yes; high positive no -> `suspicious` medium | allows, open, **with or without H03**; other high negative yes; high positive no -> `suspicious` medium | Gap 1, no fixture: an open range from 14 or later with a high negative (for example `>= 14.0.0` plus a legacy `package.manifest`) matched no row, because row 3 needs a bounded range and row 4 needed H03. | A high negative with an open range is the same evidence whatever the floor; row 3 gives `suspicious` medium for the bounded case, so the open case should match it. |
| 8 | allows, open from 14+; no; no / n/a -> `ok` low | allows, open, **H03 not applicable (floor 14 or later, or target below 14)**; no; no / n/a -> `ok` low | Gap 2, no fixture: an open range from below 14 with a target of 13 or lower matched no row, because row 8 needed a 14+ floor and row 9 needs a 14+ target. | Below 14 the new-backoffice checks are skipped (design 7.4 applicability), so an open range that allows the target is plain row-8 evidence. |
| all | (no rule for an open range with no floor) | **a missing floor counts as below 14** | Gap 3, no fixture: a range such as `(, )` or `*` has no `MinVersion`, so neither "floor below 14" (row 9, H03) nor "floor 14+" (row 8) applied. | The range says nothing about the new backoffice, so it gets the cautious reading: H03 applies when the target is 14 or later. |
| fallback | (none) | anything still unmatched -> `unknown`, reason `conflicting-evidence`, evidence "no truth-table row" | All three gaps. | A verdict is always produced and never silently `ok`. After the widening above it should be unreachable; Task 17 adds an exhaustive coverage test that proves it. |

### Rule changes below the table

| Change | What forced it | Reason |
|---|---|---|
| **H13/H14 and row 1 (R27).** H13/H14 exempt rows 5 to 8, and row 1 only when it resolves to `upgrade` to a non-deprecated, non-retired version of the same id. A row-1 `no-release` with a high H13/H14 becomes `replaced` (high) and keeps its range and H15 evidence lines. Supersedes plan Task 17's "unless the row is 1 or 5-8". Marketplace retirement is per id, so an upgrade inside a retired id does not count as exempt. | No golden in the M0 set: both picks land on rows 9 and 4. `Umbraco.Community.Forms.Mailcoach 13.0.0` (deprecated and retired, range `[13.0.0, 14.0.0)` through `Umbraco.Forms.Core 13.5.0`) is the package that tells the readings apart. `Our.Umbraco.Community.Contentment 4.7.0` becomes `replaced` once R30 adds the rename leg. | Design 7.4 says "unless row 1 or 5-8 says the current version is fine", and a row-1 `no-release` never says that. Most deprecated packages are old and land on row 1, so the plan's reading would almost never meet the design AC "a package NuGet deprecated with an alternate ... is `replaced`". |
| **Server-only (R31).** Server-only means no client assets AND no H07/H09 AND no Management API refs. H07/H09 are never discarded because a package looks server-only. | Review of the M0 engine: a package whose `IPackageManifestReader` sits in a DLL with no JS and no Management API refs would have had its H09 dropped and fallen from row 5 to row 7. No golden hit it (every H09 package also ships JS). | Design 7.4 says missing H07/H09 never makes a server-only package `suspicious`. It never says found H07/H09 should be ignored. |
| **H13/H14 never override `unknown` / `unsafe-archive` (R32).** | Review of the M0 engine. No golden. | An unsafe archive was not inspected, so nothing is known about its contents; a replacement verdict would hide a security outcome. |

### Acceptance-criterion and heuristic-definition changes

These change what a golden expects or how a heuristic is defined, not a table row.

| Change | Fixture that forced it | Reason |
|---|---|---|
| Design AC "Contentment 6.2.1 at 17.7.2 -> `upgrade` to 6.3.3, not 7.x" becomes `Umbraco.Community.Contentment 6.2.1 -> ok` (high, row 5). G6 is carried by the R19 fixture `Umbraco.Community.Contentment 5.2.1 -> upgrade 6.3.4` at 17.7.2 (no 7.x clears, so `HighestCompatible` is empty and 7.2.2 is the highest stable, flagged "does not support"), and by `5.2.1 @ 18.3.0 -> upgrade 6.1.4 (highest overall 7.2.2)`. (Rulings R19 as amended, R25.) | `Umbraco.Community.Contentment 6.2.1`: its net10.0 dependency group declares `Umbraco.Cms.*` at `[17.0.0, 18.0.0)`. | The range allows 17.7.2, so the table gives row 7 on metadata and row 5 with H09. Making it `upgrade` would mean declaring an allowing range as excluding, which breaks row 1 for every other package. Contentment 7.x declares `[18.0.0, 19.0.0)`, so "not 7.x" is only testable at an 18 target. That test has its own caveat (R25): 6.1.x declares `[17.0.0, 19.0.0)`, which looks over-broad, so the 6.1.4 pick may be a false upgrade. |
| H09 is "a type defined in the target-TFM `lib/` DLL of the package or an Umbraco dependency implements the interface type reference `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader` (resolution scope: assembly reference `Umbraco.Infrastructure`)". The `PackageManifest` leg is dropped. Weight: high positive. (Ruling R23.) | S3 control set: `Umbraco.Community.SimpleDashboards 17.2.0` (the design's H09 showcase) and the two S3 "false positives", `Umbraco.Community.Contentment 6.3.3` and `Skybrud.Umbraco.Redirects 17.0.5`. | Every H09 hit implements `IPackageManifestReader`, the v14+ C# alternative to shipping `umbraco-package.json`. Matching any type reference would also count stray uses, and `PackageManifest` is also the v10-13 model in `Umbraco.Cms.Core.Manifest`. Zero false positives is by construction, not measured (see "M0 decisions" above). |
| H08 is suppressed only by evidence in the package itself (its own H07 or H09). Dependency-only H07 or H09 next to the package's own H08 gives row 2 (`unknown`, conflicting evidence). (Ruling R23.) | No golden: none has its own legacy `package.manifest` and a dependency with the new registration. The rule comes from the S3 review. | Design 7.4 defines H08 as "legacy `package.manifest` only ... with no H07 or H09", and H07/H09 count dependencies. Read literally, a dependency's new registration would hide the package's own legacy manifest. |
| H07 matches `staticwebassets/**/umbraco-package.json` only, as design 7.4 H07 says. | M0 review (M4): the first M0 engine matched `umbraco-package.json` anywhere in the nupkg. No golden changed (every H07 hit is under `staticwebassets/`). | A manifest outside `staticwebassets/` is not served by the static web assets pipeline, so it does not register anything. |
| astroboard `--no-inspect` is row 4 (`suspicious` medium), not row 9. (Ruling R26.) | `astroboard 1.1.3 --no-inspect`: it depends on `Umbraco.Cms.Web.BackOffice`, so H05 (metadata evidence) fires without inspection and row 4 matches first. | First match wins. Plan Task 21's `NoInspect_AstroboardShape_IsRow9` is a different, synthetic shape without the BackOffice dependency: it expects `suspicious` low per R1 (H03 plus H06 failing from the nuspec groups), and gets a twin with a net10.0 group that expects `ok` low. |

### Readings applied without a change

- **H06 (ruling R1):** H06 fails when no `lib/` TFM equals the target major's own TFM (net10.0 for 17). Without inspection the nuspec dependency-group TFMs stand in. Row 9 is `suspicious` low when H03 holds and H06 fails.
- **H04 depth (rulings R3, R34):** a direct dependency is depth 1 and the walk reaches `Umbraco.Cms.*` at depth 3 at most. Each step passes on the remaining depth; it never restarts the budget. `Umbraco.Community.Contentment 4.7.0` needs all 3 (shim, Our, Our.Core, `Umbraco.Cms.Web.Website`).
