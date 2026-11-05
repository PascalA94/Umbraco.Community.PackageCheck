# S4 fixtures

Public files, fetched read-only from GitHub on 2026-11-03. Licences verified the same day with `gh api repos/<repo>/license` and by reading the upstream licence file.

| Fixture | Source repo | Path | Last commit touching the file | Repo HEAD when fetched | Licence |
|---|---|---|---|---|---|
| public-moq.slnx | devlooped/moq | Moq.slnx | a7989c000109f7c1cdc355af05761769751cc26f | bdf16e758e8022c40cef2bfd025181f845bbd2a9 | MIT (license.txt) |
| public-aspnet-api-versioning.slnx | dotnet/aspnet-api-versioning | asp.slnx | b064555b89c67ef890ac59f847046473e7f80ea0 | 1a22bb4281c03d44f387169eefa46765a6219118 | MIT (LICENSE.txt) |
| public-umbraco-cms.sln | umbraco/Umbraco-CMS | umbraco.sln | bfdf4a81c8d413d1f20feac0b8f6f737b0070ec5 | 985aff9fe7224a742782c0c72e3ed471a2dd8d1f | MIT (LICENSE.md) |

The files were fetched from the default branch, so their content matches the "last commit touching the file" SHA only if no later commit changed them between that listing and the fetch (same session).

Hand-written, no upstream: `hand-nested.slnx`, `hand-nested-malformed.slnx` (deliberately invalid: Folder inside Folder), `hand-folders.sln`.
