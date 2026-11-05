using Umbraco.Cms.Infrastructure.Manifest;

namespace PackageManifestOnlyStub;

// References the manifest model and the reader interface without implementing it: ruling R23 says neither is H09.
public class UsesPackageManifest
{
    public PackageManifest? Manifest { get; set; }
    public IPackageManifestReader? Reader { get; set; }
}
