using NuGet.Frameworks;
using NuGet.Versioning;
using PackageCheck.Core.Targets;

namespace PackageCheck.Core.Tests;

internal static class TestTargets
{
    public static TargetVersion Target(string version, string tfm = "net10.0")
    {
        var v = NuGetVersion.Parse(version);
        return new TargetVersion(version, v, NuGetFramework.Parse(tfm), v.IsPrerelease);
    }
}
