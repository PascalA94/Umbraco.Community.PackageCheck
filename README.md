# Umbraco Package Check

A dotnet tool that tells you, before you upgrade an Umbraco site, which of its NuGet packages have a release for the target Umbraco version, which need an upgrade or a replacement, and which look suspicious. Every verdict shows its evidence.

A community package, not made or supported by Umbraco HQ. Requires the .NET 8 SDK or later.

```sh
dotnet tool install -g Umbraco.Community.PackageCheck
umbraco-package-check scan ./MySite --target next-major
```

- Reads `.sln`, `.slnx`, `.csproj` or a folder, including central package management and lock files.
- Checks NuGet metadata, the Umbraco Marketplace and the package contents (read as an archive, never loaded).
- Console, JSON (schema in [`schema/report.schema.json`](https://github.com/PascalA94/Umbraco.Community.PackageCheck/blob/main/schema/report.schema.json)) and markdown reports.
- `--fail-on suspicious|upgrade|no-release` for CI. A failed source exits 3, so CI never passes on missing data.

The tool only reports; it never changes your projects. Code rules are not checked; see [Umbraco17Check](https://www.nuget.org/packages/Umbraco17Check) for those.

Full documentation: https://github.com/PascalA94/Umbraco.Community.PackageCheck
