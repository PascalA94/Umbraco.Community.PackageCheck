// Spike S2: inspect nupkgs as zip streams. Hostile-input rules: no extraction to disk,
// no assembly loading, path-safety check, byte-counting reads with per-entry and total caps.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const long PerEntryCap = 50L * 1024 * 1024;
const long TotalCap = 500L * 1024 * 1024;
const int EntryCountCap = 20_000;

var dir = args[0];
var results = new List<object>();
foreach (var file in Directory.GetFiles(dir, "*.nupkg").OrderBy(f => f, StringComparer.Ordinal))
{
    var bytesRead = 0L;
    string sha;
    using (var h = File.OpenRead(file)) sha = Convert.ToBase64String(SHA512.HashData(h));

    using var fs = File.OpenRead(file);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
    var unsafeEntries = new List<string>();
    if (zip.Entries.Count > EntryCountCap) throw new InvalidDataException("entry cap");

    string ReadText(ZipArchiveEntry e)
    {
        using var s = e.Open();
        var ms = new MemoryStream();
        var buf = new byte[81920]; int n; long entry = 0;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            entry += n; bytesRead += n;
            if (entry > PerEntryCap || bytesRead > TotalCap) throw new InvalidDataException("unsafe-archive");
            ms.Write(buf, 0, n);
        }
        return Encoding.UTF8.GetString(ms.ToArray()).TrimStart('﻿');
    }

    bool IsUnsafe(string name) =>
        name.StartsWith('/') || name.StartsWith('\\') || (name.Length > 1 && name[1] == ':')
        || name.Split('/', '\\').Contains("..");

    var names = zip.Entries.Select(e => e.FullName).ToList();
    foreach (var n in names) if (IsUnsafe(n)) unsafeEntries.Add(n);

    bool Under(string n, string root) => n.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    var umbracoPackageJson = names.Where(n => n.EndsWith("/umbraco-package.json", StringComparison.OrdinalIgnoreCase) || n.Equals("umbraco-package.json", StringComparison.OrdinalIgnoreCase)).ToList();
    var packageManifest = names.Where(n => n.EndsWith("/package.manifest", StringComparison.OrdinalIgnoreCase) || n.Equals("package.manifest", StringComparison.OrdinalIgnoreCase)).ToList();
    var propsFiles = names.Where(n => Regex.IsMatch(n, @"^build[^/]*/Microsoft\.AspNetCore\.StaticWebAssets\.props$", RegexOptions.IgnoreCase)).ToList();
    var libTfms = names.Where(n => n.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)).Select(n => n.Split('/')).Where(p => p.Length > 2).Select(p => p[1]).Distinct().OrderBy(x => x).ToList();
    var jsAssets = names.Where(n => (n.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
        && (Under(n, "staticwebassets/") || n.Contains("/staticwebassets/", StringComparison.OrdinalIgnoreCase) || Under(n, "App_Plugins/") || n.Contains("/App_Plugins/", StringComparison.OrdinalIgnoreCase))).ToList();

    var nuspecEntry = zip.Entries.First(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/'));
    var nuspec = XDocument.Parse(ReadText(nuspecEntry));
    var md = nuspec.Root!.Elements().First(e => e.Name.LocalName == "metadata");
    string M(string n) => md.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value ?? "";
    var deps = md.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies")?.Elements().Where(e => e.Name.LocalName == "group")
        .Select(g => new { tfm = (string?)g.Attribute("targetFramework") ?? "(none)", deps = g.Elements().Select(d => $"{(string?)d.Attribute("id")} {(string?)d.Attribute("version")}").ToList() }).ToList();
    var flatDeps = md.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies")?.Elements().Where(e => e.Name.LocalName == "dependency")
        .Select(d => $"{(string?)d.Attribute("id")} {(string?)d.Attribute("version")}").ToList();

    var basePaths = new List<object>();
    foreach (var p in propsFiles)
    {
        var x = XDocument.Parse(ReadText(zip.GetEntry(p)!));
        var bp = x.Descendants().Where(e => e.Name.LocalName == "BasePath").Select(e => e.Value).Distinct().ToList();
        var sr = x.Descendants().Where(e => e.Name.LocalName == "SourceType").Select(e => e.Value).Distinct().ToList();
        basePaths.Add(new { props = p, basePaths = bp });
    }

    results.Add(new
    {
        file = Path.GetFileName(file), id = M("id"), version = M("version"), sha512 = sha, bytes = new FileInfo(file).Length,
        entries = names.Count, unsafeEntries, libTfms, umbracoPackageJson, packageManifest, propsFiles, basePaths,
        jsAssetCount = jsAssets.Count, jsAssetsSample = jsAssets.Take(4).ToList(),
        dependencyGroups = deps, flatDependencies = flatDeps, bytesReadFromEntries = bytesRead
    });
}
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
