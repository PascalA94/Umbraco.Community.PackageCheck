using System.Security.Cryptography;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageCheck.Core.Facts;
using PackageCheck.Core.Model;
using PackageCheck.Tests.Support;

namespace PackageCheck.Inspect.Tests;

public class NupkgInspectorTests
{
    private static readonly PackageIdentity Id = new("Test.Package", NuGetVersion.Parse("1.0.0"));

    private static string Props(string basePath) => $"""
        <Project>
          <ItemGroup>
            <StaticWebAsset Include="$(MSBuildThisFileDirectory)..\staticwebassets\x.js">
              <SourceType>Package</SourceType>
              <BasePath>{basePath}</BasePath>
            </StaticWebAsset>
          </ItemGroup>
        </Project>
        """;

    private static Task<InspectionResult> Inspect(string path, ArchiveLimits? limits = null) =>
        new NupkgInspector(limits ?? ArchiveLimits.Default)
            .InspectAsync(Id, new NupkgHandle(path, IsTemporary: false, new FileInfo(path).Length), TestContext.Current.CancellationToken);

    private static async Task<PackageSourceException> Unsafe(string path, ArchiveLimits? limits = null)
    {
        var ex = await Assert.ThrowsAsync<PackageSourceException>(() => Inspect(path, limits));
        Assert.Equal(UnknownReason.UnsafeArchive, ex.Reason);
        return ex;
    }

    [Fact]
    public async Task UmbracoPackageJson_UnderStaticWebAssets_Found()
    {
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0").WithFile("staticwebassets/umbraco-package.json", "{}").Build());
        Assert.True(r.HasUmbracoPackageJson);
        Assert.Contains("staticwebassets/umbraco-package.json", r.ManifestPaths);
        Assert.Equal("Test.Package", r.PackageId);
        Assert.Equal("1.0.0", r.Version);
    }

    [Fact]
    public async Task UmbracoPackageJson_OutsideStaticWebAssets_NotCounted()
    {
        // Truth table changes: H07 matches staticwebassets/** only; anything else is not served.
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0").WithFile("content/umbraco-package.json", "{}").Build());
        Assert.False(r.HasUmbracoPackageJson);
    }

    [Theory]
    [InlineData("content/App_Plugins/X/package.manifest")]
    [InlineData("App_Plugins/X/package.manifest")]
    [InlineData("staticwebassets/package.manifest")]
    public async Task LegacyManifest_UnderContentAppPlugins_Found(string path)
    {
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0").WithFile(path, "{}").Build());
        Assert.True(r.HasLegacyManifest);
        Assert.False(r.HasUmbracoPackageJson);
        Assert.Contains(path, r.ManifestPaths);
    }

    [Fact]
    public async Task DualMode_BothFlagsTrue()
    {
        var r = await Inspect(NupkgBuilder.Create("Crumpled.RobotsTxt", "3.1.1")
            .WithFile("staticwebassets/umbraco-package.json", "{}")
            .WithFile("staticwebassets/package.manifest", "{}")
            .WithFile("build/Microsoft.AspNetCore.StaticWebAssets.props", Props("App_Plugins/Crumpled.RobotsTxt"))
            .Build());
        Assert.True(r.HasUmbracoPackageJson);
        Assert.True(r.HasLegacyManifest);
        Assert.Equal("App_Plugins/Crumpled.RobotsTxt", r.StaticWebAssetBasePath);
    }

    [Theory]
    [InlineData("build/Microsoft.AspNetCore.StaticWebAssets.props")]
    [InlineData("buildTransitive/Microsoft.AspNetCore.StaticWebAssets.props")]
    public async Task BasePath_ReadFromBuildProps(string propsPath)
    {
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0").WithFile(propsPath, Props(" _content/A ")).Build());
        Assert.Equal("_content/A", r.StaticWebAssetBasePath);
        Assert.Empty(r.Notes ?? []);
    }

    [Theory]
    [InlineData("staticwebassets/App_Plugins/A/a.js", true)]
    [InlineData("staticwebassets/scripts/a.mjs", true)]
    [InlineData("content/App_Plugins/A/a.js", true)]
    [InlineData("App_Plugins/A/a.js", true)]
    [InlineData("lib/net10.0/a.js", false)]
    [InlineData("staticwebassets/App_Plugins/A/readme.txt", false)]
    public async Task ClientAssets_JsUnderStaticWebAssets(string path, bool expected)
    {
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0").WithFile(path, "x").Build());
        Assert.Equal(expected, r.HasClientAssets);
    }

    [Fact]
    public async Task LibTfms_Listed()
    {
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0")
            .WithFile("lib/net9.0/A.dll", [1])
            .WithFile("lib/net10.0/A.dll", [1])
            .WithFile("lib/netstandard2.0/_._", [])
            .WithFile("lib/readme.txt", "not a folder")
            .Build());
        Assert.Equal(["net10.0", "net9.0", "netstandard2.0"], r.LibTfms);
    }

    [Fact]
    public async Task Metapackage_NoLib_EmptyTfms()
    {
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0").Build());
        Assert.Empty(r.LibTfms);
        Assert.False(r.HasClientAssets);
        Assert.Null(r.StaticWebAssetBasePath);
    }

    [Fact]
    public async Task ContentHash_IsBase64Sha512OfTheFile()
    {
        var path = NupkgBuilder.Create("A", "1.0.0").Build();
        var r = await Inspect(path);
        Assert.Equal(Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(path))), r.ContentHash);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("lib/../../evil.dll")]
    [InlineData("lib\\..\\evil.dll")]
    public async Task ZipSlipEntry_IsUnsafeArchive(string name) =>
        await Unsafe(NupkgBuilder.Create("A", "1.0.0").WithRawEntryName(name).Build());

    [Theory]
    [InlineData("/etc/x")]
    [InlineData("\\x")]
    [InlineData("C:\\x")]
    [InlineData("c:/x")]
    public async Task RootedEntry_IsUnsafeArchive(string name) =>
        await Unsafe(NupkgBuilder.Create("A", "1.0.0").WithRawEntryName(name).Build());

    [Fact]
    public async Task EntryCountOverCap_IsUnsafeArchive()
    {
        var b = NupkgBuilder.Create("A", "1.0.0");
        for (var i = 0; i < 10; i++) b.WithFile($"content/f{i}.txt", "x");
        // 10 files plus the nuspec is 11 entries.
        await Unsafe(b.Build(), ArchiveLimits.Default with { MaxEntries = 10 });
    }

    [Fact]
    public async Task EntryCountAtCap_IsOk()
    {
        var b = NupkgBuilder.Create("A", "1.0.0");
        for (var i = 0; i < 9; i++) b.WithFile($"content/f{i}.txt", "x");
        await Inspect(b.Build(), ArchiveLimits.Default with { MaxEntries = 10 });
    }

    [Fact]
    public async Task EntryOverSizeCap_IsSkippedWithEvidenceNote()
    {
        var path = NupkgBuilder.Create("A", "1.0.0")
            .WithFile("build/Microsoft.AspNetCore.StaticWebAssets.props", Props("App_Plugins/A") + new string(' ', 2048))
            .Build();
        var r = await Inspect(path, ArchiveLimits.Default with { MaxEntryBytes = 1024 });
        Assert.Null(r.StaticWebAssetBasePath);
        Assert.Contains(r.Notes!, n => n.Contains("build/Microsoft.AspNetCore.StaticWebAssets.props") && n.Contains("size cap"));
    }

    [Fact]
    public async Task NothingWrittenToDisk()
    {
        var path = NupkgBuilder.Create("A", "1.0.0")
            .WithFile("staticwebassets/umbraco-package.json", "{}")
            .WithFile("build/Microsoft.AspNetCore.StaticWebAssets.props", Props("App_Plugins/A"))
            .WithFile("lib/net10.0/A.dll", [1, 2, 3])
            .Build();
        // A file count over the whole temp folder would race other processes, so look for anything the inspector could
        // have extracted: a new file next to the nupkg, or a new temp file named like one of its entries.
        string[] entryNames = ["umbraco-package.json", "Microsoft.AspNetCore.StaticWebAssets.props", "A.dll", "A.nuspec"];
        var tmpBefore = Directory.GetFiles(Path.GetTempPath(), "*", SearchOption.TopDirectoryOnly).ToHashSet();
        var siblingsBefore = Directory.GetFiles(NupkgBuilder.Folder).ToHashSet();
        var cwdBefore = Directory.GetFiles(Environment.CurrentDirectory).ToHashSet();

        await Inspect(path);

        Assert.DoesNotContain(Directory.GetFiles(NupkgBuilder.Folder), f => !siblingsBefore.Contains(f) && !f.EndsWith(".nupkg"));
        Assert.DoesNotContain(Directory.GetFiles(Path.GetTempPath()).Where(f => !tmpBefore.Contains(f)), f => entryNames.Contains(Path.GetFileName(f)));
        Assert.DoesNotContain(Directory.GetFiles(Environment.CurrentDirectory), f => !cwdBefore.Contains(f));
    }

    [Fact]
    public async Task ManifestEntriesOverOneMegabyte_AreNotParsed()
    {
        // Manifests are recorded by name only. A 2 MB manifest under a 1 MB total budget proves no byte of it is read.
        var big = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(big);
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0")
            .WithFile("staticwebassets/umbraco-package.json", big)
            .WithFile("App_Plugins/A/package.manifest", big)
            .Build(), ArchiveLimits.Default with { MaxTotalBytes = 1024 * 1024, MaxEntryBytes = 4 * 1024 * 1024 });
        Assert.True(r.HasUmbracoPackageJson);
        Assert.True(r.HasLegacyManifest);
    }

    [Fact]
    public async Task LyingEntrySize_StoppedByByteCount()
    {
        // The central directory says 1 KB; the entry inflates to 2 MB, past the 1 MB per-entry cap. .NET's zip reader
        // stops inflating at the declared size, so the lie truncates the entry rather than inflating it; CountingReadStream
        // (tested directly in CountingReadStreamTests) is the second line if a runtime ever reads further. Either way no
        // more than the cap is read and the package is never judged on the entry's contents.
        var props = Props("App_Plugins/A") + new string(' ', 2 * 1024 * 1024);
        var path = NupkgBuilder.Create("A", "1.0.0")
            .WithLyingSize("build/Microsoft.AspNetCore.StaticWebAssets.props", System.Text.Encoding.UTF8.GetBytes(props), declaredSize: 64)
            .Build();
        try
        {
            var r = await Inspect(path, ArchiveLimits.Default with { MaxEntryBytes = 1024 * 1024 });
            Assert.Null(r.StaticWebAssetBasePath);
            Assert.Contains(r.Notes!, n => n.Contains("not read"));
        }
        catch (PackageSourceException ex)
        {
            Assert.Equal(UnknownReason.UnsafeArchive, ex.Reason);
        }
    }

    [Fact]
    public async Task TotalBytesOverCap_IsUnsafeArchive()
    {
        // Five props files of 300 KB each (every build*/ folder is read) against a 1 MB total budget.
        var b = NupkgBuilder.Create("A", "1.0.0");
        foreach (var folder in new[] { "build", "buildTransitive", "buildMultiTargeting", "buildCrossTargeting", "buildExtra" })
            b.WithFile($"{folder}/Microsoft.AspNetCore.StaticWebAssets.props", Props("A") + new string(' ', 300 * 1024));
        var ex = await Unsafe(b.Build(), ArchiveLimits.Default with { MaxTotalBytes = 1024 * 1024 });
        Assert.Contains("total size cap", ex.Message);
    }

    [Fact]
    public async Task Props_WithDoctype_IsRejectedNoExpansion()
    {
        // An external entity pointing at a local file and a URL: with DTDs prohibited neither is resolved.
        var marker = Path.Combine(Path.GetTempPath(), $"pc-xxe-{Guid.NewGuid():N}.txt");
        var props = $"""
            <?xml version="1.0"?>
            <!DOCTYPE Project [ <!ENTITY a SYSTEM "file://{marker}"> <!ENTITY b SYSTEM "http://127.0.0.1:9/x"> ]>
            <Project><ItemGroup><StaticWebAsset Include="x"><BasePath>&a;&b;</BasePath></StaticWebAsset></ItemGroup></Project>
            """;
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0").WithFile("build/Microsoft.AspNetCore.StaticWebAssets.props", props).Build());
        Assert.Null(r.StaticWebAssetBasePath);
        Assert.Contains(r.Notes!, n => n.Contains("Microsoft.AspNetCore.StaticWebAssets.props") && n.Contains("not read"));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task NotAZip_IsUnsafeArchive()
    {
        var path = Path.Combine(NupkgBuilder.Folder, $"garbage-{Guid.NewGuid():N}.nupkg");
        Directory.CreateDirectory(NupkgBuilder.Folder);
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4], TestContext.Current.CancellationToken);
        await Unsafe(path);
    }

    [Fact]
    public async Task NupkgOverSizeCap_ThrowsTooLarge()
    {
        var path = NupkgBuilder.Create("A", "1.0.0").WithFile("content/x.txt", new byte[4096]).Build();
        await Assert.ThrowsAsync<NupkgTooLargeException>(() => Inspect(path, ArchiveLimits.Default with { MaxNupkgBytes = 100 }));
    }

    [Fact]
    public async Task UnparseableLibFolder_IsDroppedWithANote() // security review M3 (S3)
    {
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0")
            .WithFile("lib/portable-net45+win-8/a.dll", [1])
            .WithFile("lib/net10.0/a.dll", [1])
            .Build());
        Assert.Equal(["net10.0"], r.LibTfms);
        Assert.DoesNotContain(r.LibScans!, s => s.Tfm.StartsWith("portable", StringComparison.Ordinal));
        Assert.Contains(r.Notes!, n => n.Contains("not a recognised target framework"));
    }

    // Security review M3 (S7): entry names and BasePath are package text. Control and format characters (ESC, OSC, bidi
    // overrides) are escaped and long values truncated before they reach evidence, so no report can be driven by them.
    [Fact]
    public async Task PackageText_IsEscapedAndCapped()
    {
        const string esc = "\u001b]0;OWNED\u0007\u202e";
        var r = await Inspect(NupkgBuilder.Create("A", "1.0.0")
            .WithFile($"build{esc}/Microsoft.AspNetCore.StaticWebAssets.props", "<!DOCTYPE x><x/>")
            // XML forbids C0 controls, so the BasePath carries what XML allows: a bidi override and length.
            .WithFile("build/Microsoft.AspNetCore.StaticWebAssets.props", Props($"App_Plugins/\u202e{new string('x', 5000)}"))
            .WithFile($"App_Plugins/{esc}/package.manifest", "{}")
            .Build());
        var all = r.Notes!.Concat(r.ManifestPaths).Append(r.StaticWebAssetBasePath!).ToList();
        Assert.DoesNotContain(all, t => t.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format));
        Assert.Contains(all, t => t.Contains("\\u001b"));
        Assert.True(r.StaticWebAssetBasePath!.Length <= 200);
    }
}

