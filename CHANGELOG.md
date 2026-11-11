# Changelog

All notable changes to Umbraco Package Check are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.1.1] - 2026-11-11

Documentation only. No code changes.

### Changed
- README and screenshots: re-captured at 1400px with the full command line and no empty space, added a CI screenshot (`--fail-on` exit code and JSON counts), moved the `explain` screenshot next to Verdicts, and rewrote the image alt text and captions.

## [0.1.0] - 2026-11-06

First release.

### Added
- `scan [paths...]`: reads `.sln`, `.slnx`, `.csproj` or folders (central package management, lock files, MSBuild properties) and judges every package against a target Umbraco version: `ok`, `upgrade`, `no-release`, `suspicious`, `replaced`, `unknown` or `not-umbraco`, always with the evidence.
- Targets: a major (`17`), an exact version, `current`, `next-major` or `latest-lts`, resolved per site. A prerelease target is compared by its release base.
- Package inspection without loading code: manifests, static web assets and assembly type references, read from the nupkg as an untrusted archive.
- Upgrade picks: the lowest package major that supports the target, at its highest stable version, preferring the major made for the target (its Umbraco range starts in the target's major, then it is built for the target's .NET version). For example, uSync 13 at Umbraco 17 gets 17.6.1, and Contentment 5.2.1 at 18.3.0 gets 7.2.2.
- Community overrides in `overrides/` (CC0), republished to this repo's `overrides` release on every merge to `main`: the tool downloads them, checks the sha256 and caches them for 24 hours, falling back to the cached copy and then the bundled snapshot. They never fail a run. Local overrides with `--overrides`. Every override is labelled, linked and shown beside the computed verdict.
- `--msbuild`: exact resolved versions, and the transitive graph with `--transitive`, from `dotnet list package` (needs a prior restore; never restores). Falls back to reading project files with a warning.
- Console, JSON (schema v1, `schema/report.schema.json`) and markdown reports, with a site x package matrix for several sites.
- `--fail-on` with exit codes 0, 1, 2 and 3; a failed source with `--fail-on` set exits 3.
- Cache, `--offline`, `--no-cache`, `--private-ids`.
- `explain <packageId> [version] --target <v>` and `cache info|clear`.
- README, `HEURISTICS.md`, contributing and security notes, issue templates and screenshots.

### Security
- `System.Security.Cryptography.Pkcs` (a transitive dependency of NuGet.Packaging) is pinned to 8.0.2, fixing GHSA-846f-f764-76mx (High) and GHSA-2fmv-rv2c-59x7 (Moderate) in 8.0.1. The build audits transitive packages and fails on any advisory.
- `--msbuild` finds `dotnet` on `PATH` (or `DOTNET_HOST_PATH`), never in the scanned folder, and passes `--no-restore` on every SDK that would otherwise restore.
- Override evidence links with user info are rejected, and a release's Umbraco majors table is only used when it looks sane.
