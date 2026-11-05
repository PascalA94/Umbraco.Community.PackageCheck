using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PackageCheck.Cli.Tests;

/// <summary>Pitfalls P1 and P2 [Critical]: no static mutable state, no service locator (global constraints).</summary>
public class ArchitectureTests
{
    private static readonly string[] AssemblyNames =
        ["PackageCheck.Cli", "PackageCheck.Core", "PackageCheck.Inputs", "PackageCheck.Sources", "PackageCheck.Inspect", "PackageCheck.Report"];

    private static IEnumerable<Type> ProductTypes() =>
        AssemblyNames.Select(Assembly.Load).SelectMany(a => a.GetTypes());

    // Ruling R16: compiler- and source-generator-made types (closures, regex and JSON contexts) are excluded.
    private static bool Generated(Type t)
    {
        for (var x = t; x is not null; x = x.DeclaringType)
            if (x.IsDefined(typeof(CompilerGeneratedAttribute), false) || x.IsDefined(typeof(GeneratedCodeAttribute), false)
                || x.Name.Contains('<', StringComparison.Ordinal))
                return true;
        return false;
    }

    [Fact]
    public void Assemblies_AreLoaded() => Assert.Equal(AssemblyNames.Length, AssemblyNames.Select(Assembly.Load).Count());

    [Fact]
    public void NoStaticMutableFields()
    {
        var offenders = ProductTypes()
            .Where(t => !Generated(t))
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsInitOnly && !f.IsLiteral && !f.IsDefined(typeof(CompilerGeneratedAttribute), false))
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoServiceLocator()
    {
        var offenders = ProductTypes()
            .Where(t => !Generated(t) && t.FullName != "PackageCheck.Cli.Commands.ServiceRegistration")
            .SelectMany(t => t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(c => c.GetParameters().Any(p => p.ParameterType == typeof(IServiceProvider)))
                .Select(c => t.FullName!))
            .ToList();

        Assert.Empty(offenders);
    }
}
