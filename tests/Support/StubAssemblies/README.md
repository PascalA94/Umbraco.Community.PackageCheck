# Stub assemblies

Tiny `netstandard2.0` libraries for `MetadataTypeScannerTests` (plan Task 20). They are compiled against stub types
with Umbraco's exact namespaces, type names and assembly names, so no Umbraco package is referenced:

| DLL | What it holds |
|---|---|
| `ManifestReaderStub.dll` | a class implementing `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader` (assembly `Umbraco.Infrastructure`) |
| `ManagementApiStub.dll` | a class deriving from `Umbraco.Cms.Api.Management.Controllers.ManagementApiControllerBase` |
| `PackageManifestOnlyStub.dll` | references `PackageManifest` and `IPackageManifestReader` without implementing the interface (ruling R23: not H09) |
| `PlainStub.dll` | a plain class |

The DLLs are checked in so tests never build code at run time. To rebuild them after changing `src/`:

```sh
tests/Support/StubAssemblies/build.sh
```

The `src/` folder has its own `Directory.Build.props`, so the repo's build settings do not apply to it.
