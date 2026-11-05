# Changelog

All notable changes to Umbraco Package Check are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

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
