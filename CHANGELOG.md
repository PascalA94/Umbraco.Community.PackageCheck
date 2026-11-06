# Changelog

All notable changes to Umbraco Package Check are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.1.0] - unreleased

### Added
- Community overrides from the `umbraco-package-compat` release: downloaded, checked against its sha256, cached for 24 hours, with the cached copy and then the bundled snapshot as fallbacks. It never fails a run. The release's Umbraco majors table replaces the bundled one when it is newer.
- `--msbuild`: exact resolved versions, and the transitive graph with `--transitive`, from `dotnet list package` (needs a prior restore; never restores). Falls back to reading project files with a warning.
- README, `HEURISTICS.md`, issue templates, contributing and security notes, screenshots and `umbraco-marketplace.json`.

### Changed
- Upgrade picks prefer the package major made for the target: first one whose Umbraco range starts in the target's major, then one built for the target's .NET version. An older major with an open or over-broad range no longer wins (uSync 13 at Umbraco 17 gets 17.6.1, not 14.2.3; at 18 it gets 18.3.0, not 17.6.1; Contentment 5.2.1 at 18.3.0 gets 7.2.2, not 6.1.4).

### Security
- `System.Security.Cryptography.Pkcs` (a transitive dependency of NuGet.Packaging) is pinned to 8.0.2, fixing GHSA-846f-f764-76mx (High) and GHSA-2fmv-rv2c-59x7 (Moderate) in 8.0.1. The build now audits transitive packages and fails on any advisory.
- `--msbuild` finds `dotnet` on `PATH` (or `DOTNET_HOST_PATH`), never in the scanned folder, decides `--no-restore` from the SDK version instead of retrying, and falls back to reading files if `dotnet` can't start or prints something unexpected.
- Override evidence links with user info (`https://github.com@evil.example/`) are rejected. A community release's majors table is only used when it looks sane (dated no later than tomorrow, lists every known major, has a supported LTS).

## [0.1.0-rc.1] - unreleased

First release candidate, published to rehearse the release workflow before 0.1.0.

### Added
- `scan [paths...]`: reads `.sln`, `.slnx`, `.csproj` or folders (central package management, lock files, MSBuild properties) and judges every package against a target Umbraco version: `ok`, `upgrade`, `no-release`, `suspicious`, `replaced`, `unknown` or `not-umbraco`, always with the evidence.
- Targets: a major (`17`), an exact version, `current`, `next-major` or `latest-lts`, resolved per site.
- Package inspection without loading code: manifests, static web assets and assembly type references, read from the nupkg as an untrusted archive.
- Curated overrides (bundled seed plus `--overrides`), always labelled, with the computed verdict kept.
- Console, JSON (schema v1, `schema/report.schema.json`) and markdown reports, with a site x package matrix for several sites.
- `--fail-on` with exit codes 0, 1, 2 and 3; a failed source with `--fail-on` set exits 3.
- Cache, `--offline`, `--no-cache`, `--private-ids`.
- `explain <packageId> [version] --target <v>` and `cache info|clear`.
