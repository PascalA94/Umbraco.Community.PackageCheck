# Heuristics reference (0.1)

How Umbraco Package Check turns evidence into a verdict. Each check adds a line of evidence with a result (positive, negative or info) and a weight (high, medium, low or info). The verdict comes from the truth table below, never from one check on its own. `--verbose` and `explain` print every line.

Source of truth: `src/PackageCheck.Core/Heuristics/` and `src/PackageCheck.Core/Verdicts/TruthTable.cs`. The design (`docs/design.md`, section 7.4) and the confirmed changes to it (`docs/spikes.md`, "Truth table changes") explain why.

## Checks

| ID | Check | Source | 0.1 | Result and weight |
|---|---|---|---|---|
| H01 | The declared `Umbraco.Cms.*` range allows the target, using the dependency group NuGet would pick for the target's TFM | NuGet metadata | Built | positive: medium if bounded, low if open |
| H02 | The range has an upper bound that excludes the target | NuGet metadata | Built | negative, high |
| H03 | An open range starts below 14 while the target is 14 or later (a range with no floor counts as below 14) | NuGet metadata | Built | negative, low |
| H04 | No direct `Umbraco.Cms.*` dependency: walk the dependencies, up to depth 3, and use the ranges found there | NuGet metadata | Built | info (H01 and H02 use what it finds) |
| H05 | Depends on a core package removed with the new backoffice (`Umbraco.Cms.Web.BackOffice`) | NuGet metadata | Built | negative, high |
| H06 | No `lib/` folder (or, without inspection, dependency group) for the target major's own TFM, for example net10.0 for 17 | nupkg or NuGet metadata | Built | negative, low; decides row 9 and the upgrade preference |
| H07 | `staticwebassets/**/umbraco-package.json` in the package or one of its Umbraco dependencies | nupkg | Built | positive, high |
| H08 | Legacy `package.manifest` only (no H07 or H09 in the package itself) | nupkg | Built | negative, high |
| H09 | A type in the target-TFM `lib/` assembly of the package or an Umbraco dependency implements `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader` | nupkg (metadata reader, no load) | Built | positive, high |
| H10 | References types or members removed in the target | API index | Not in 0.1 | |
| H11 | References Management API types but ships no JavaScript and no manifest | nupkg | Built | negative, low (evidence only) |
| H12 | The Marketplace's latest version for the target | Marketplace | Built | info (it repeats NuGet ranges, so it is not independent) |
| H13 | Retired or renamed on the Marketplace, with an alternate | Marketplace | Built | negative, high |
| H14 | Deprecated on NuGet, with an alternate | NuGet metadata | Built | negative, high |
| H15 | Only a prerelease supports the target | NuGet metadata | Built | info |
| H16 | A community or local override matches the id, version and target | Overrides | Built | applied after the table; always labelled |
| H17 | A leftover `App_Plugins/<name>` folder in the project that shadows a package's `BasePath` | Project files | Built | negative, medium (evidence only) |
| H18 | The last release is older than the target major | NuGet metadata | Built | info |
| H19 | An HQ package (Marketplace `isHQ`) whose ranges are all bounded and allow the target | Marketplace | Built | positive, high; evidence only (it never lifts row 7 to high) |

H03, H07, H08, H09 and H11 only apply when the target is 14 or later (the new backoffice).

"Contents" (H07, H08, H09) count only when the package was opened. With `--no-inspect`, or when the package couldn't be read, they are "n/a".

## Truth table (current version)

Read top to bottom; the first row that matches wins.

| # | Range for the target | High negative (H05, H08) | High positive (H07, H09) | Verdict | Confidence |
|---|---|---|---|---|---|
| 1 | excludes (H02) | any | any | `upgrade` if a newer version clears, else `no-release` | high |
| 2 | allows | yes | yes | `unknown`, conflicting evidence | |
| 3 | allows, bounded | yes | no | `suspicious` | medium |
| 4 | allows, open (with or without H03) | yes | no | `suspicious` | medium |
| 5 | allows, bounded, or HQ (H19) | no | yes | `ok` | high |
| 6 | allows, open | no | yes | `ok` | high |
| 7 | allows, bounded | no | no / n/a | `ok` | medium |
| 8 | allows, open, H03 not applicable (floor 14 or later, or target below 14) | no | no / n/a | `ok` | low |
| 9 | allows, open from below 14, target 14+ (H03) | no | no / n/a | `suspicious` if H06 fails, else `ok` | low |
| 10 | no `Umbraco.Cms.*` in the dependency tree | | | `not-umbraco` | |

Anything that matches no row is `unknown` (conflicting evidence, "no truth-table row"). A test proves this can't happen with the rows above.

## Rules applied after the table

- **Replaced (H13, H14).** A retired, renamed or deprecated package with an alternate is `replaced` (high), naming the alternate. Rows 5 to 8 keep their verdict and only add a note. Row 1 also keeps it when the upgrade is to a version of the same id that is itself not retired or deprecated. An `unsafe-archive` unknown is never turned into `replaced`.
- **Server-only packages.** A package with no client assets, no H07/H09 and no Management API references is not marked `suspicious` for missing H07/H09.
- **Upgrade candidates** are judged with the same table. A candidate clears the target when it lands on rows 5 to 8 and is listed. The pick is the lowest clearing package major, at its highest stable version, with two preferences tried first, in order: majors whose Umbraco ranges all start in the target's major, then majors built for the target's TFM (H06 passes). A major qualifies for a preference when its highest clearing version meets it. At most 3 candidate packages are opened per package. A candidate that couldn't be opened is recommended at medium confidence, with a note.
- **Overrides (H16)** replace the computed verdict, including `unknown` from conflicting evidence: `works` gives `ok` (high); `broken` gives `no-release`, or `upgrade` when another version isn't covered by the entry; `partial` gives `suspicious`. The computed verdict stays in the report. Local overrides (`--overrides`) beat community ones for the same id and ranges.
- **Unknowns.** Offline cache misses, private feeds (401/403 or `--private-ids`), packages not found on any feed, unresolved versions, unsafe archives and source errors are `unknown` with that reason. Only `source-error` makes the run incomplete.

## Prerelease targets

A prerelease target such as `18.0.0-rc2` is compared by its release base, `18.0.0`, in every range check.
