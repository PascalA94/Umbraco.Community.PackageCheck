# Repo-shape fixtures

Synthetic. They copy the shape of real Manifesto v17 repos (project files, props, lock skeletons) with ids and versions only: no code, no settings, no client names. Each folder has an `expected.json` that `RepoShapeTests.Shape_MatchesExpected` compares against what `ProjectReader` returns.

| Shape | What it covers |
| --- | --- |
| shape-01 | `.slnx` with central package management |
| shape-02 | `.sln` with CPM and a `VersionOverride` in a test project |
| shape-03 | plain csproj versions, no solution file |
| shape-04 | `packages.lock.json` v2 with a transitive package (scanned with `Transitive`) |
| shape-05 | a test project outside `src/` |
| shape-06 | site under `cms/` next to a frontend folder |
| shape-07 | `$(UmbracoVersion)` defined in `Directory.Build.props` |
| shape-08 | a property nothing defines, so the package stays unresolved |
| shape-09 | mixed Umbraco majors, 13 and 17 |
| shape-10 | no Umbraco project; expected result is the `NoUmbracoProject` error |
