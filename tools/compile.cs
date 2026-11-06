#!/usr/bin/env dotnet
// Validates overrides/*.yml and compiles the community overrides asset (design 7.8; the overrides live in this repo
// since 2026-11-06, see docs/spikes.md).
//   dotnet run tools/compile.cs -- --check                          validate only (every pull request)
//   dotnet run tools/compile.cs -- --out dist                       also write dist/overrides.json and its .sha256
//   dotnet run tools/compile.cs -- --seed data/overrides.seed.json  also write the snapshot bundled in the tool
// A .NET file-based app rather than Python: it uses the same YAML parser and NuGet range parser as the tool,
// so a file that passes here is read the same way by umbraco-package-check.
#:package YamlDotNet@18.1.0
#:package NuGet.Versioning@7.9.0
#:package JsonSchema.Net@9.4.1
#:property PublishAot=false
#:property TargetFramework=net10.0
#:property ManagePackageVersionsCentrally=false

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using NuGet.Versioning;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

const int MaxFileBytes = 64 * 1024;

var argv = args.ToList();
var check = argv.Remove("--check");
string? Take(string flag)
{
    var i = argv.IndexOf(flag);
    if (i < 0 || i + 1 >= argv.Count) return null;
    var value = argv[i + 1];
    argv.RemoveRange(i, 2);
    return value;
}
var outDir = Take("--out");
var seedFile = Take("--seed");
if (argv.Count > 0 || (!check && outDir is null && seedFile is null))
{
    Console.Error.WriteLine("usage: dotnet run tools/compile.cs -- --check | --out <dir> | --seed <file>");
    return 2;
}

var root = FindRoot();
var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(root, "schema", "override.schema.json")));
var errors = new List<string>();
var packages = new SortedDictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

var overrideFiles = Directory.GetFiles(Path.Combine(root, "overrides"))
    .OrderBy(f => f, StringComparer.Ordinal)
    .ToList();
foreach (var path in overrideFiles)
{
    var name = "overrides/" + Path.GetFileName(path);
    // The folder's own licence and notes are not override files.
    if (Path.GetFileName(path) is "LICENSE" or "README.md") continue;
    if (!path.EndsWith(".yml", StringComparison.Ordinal)) { errors.Add($"{name}: only .yml files belong in overrides/"); continue; }
    var file = ReadOverrideFile(path, name, errors);
    if (file is null) continue;

    var id = file["id"]!.GetValue<string>();
    if (!string.Equals(Path.GetFileName(path), id.ToLowerInvariant() + ".yml", StringComparison.Ordinal))
        errors.Add($"{name}: file name must be {id.ToLowerInvariant()}.yml (the id in lower case)");
    if (!packages.TryAdd(id, file)) errors.Add($"{name}: id {id} is already defined in another file");
}

var versions = ReadVersionsTable(Path.Combine(root, "data", "umbraco-versions.json"), errors);

if (errors.Count > 0)
{
    foreach (var e in errors) Console.Error.WriteLine(e);
    Console.Error.WriteLine($"{errors.Count} problem(s) found.");
    return 1;
}
Console.WriteLine($"OK: {packages.Count} override file(s), {versions!["majors"]!.AsArray().Count} Umbraco majors.");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n"));

if (seedFile is not null)
{
    // The tool's offline snapshot: the same entries, without the majors table (the tool bundles data/ directly).
    var seed = new JsonObject
    {
        ["schemaVersion"] = 1,
        ["overrides"] = new JsonArray(packages.Values.Select(p => (JsonNode)p.DeepClone()).ToArray()),
    };
    File.WriteAllBytes(seedFile, Utf8(seed.ToJsonString(jsonOptions) + "\n"));
    Console.WriteLine($"Wrote {seedFile}.");
}
if (outDir is null) return 0;

// The asset is deterministic (no timestamps, sorted ids) so the same tag always produces the same hash.
var asset = new JsonObject
{
    ["schemaVersion"] = 1,
    ["overrides"] = new JsonArray(packages.Values.Select(p => (JsonNode)p.DeepClone()).ToArray()),
    ["umbracoVersions"] = versions.DeepClone(),
};
Directory.CreateDirectory(outDir);
var bytes = Utf8(asset.ToJsonString(jsonOptions) + "\n");
File.WriteAllBytes(Path.Combine(outDir, "overrides.json"), bytes);
var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
// Same layout as `shasum -a 256` / `sha256sum`, so either can check it.
File.WriteAllText(Path.Combine(outDir, "overrides.json.sha256"), $"{hash}  overrides.json\n");
Console.WriteLine($"Wrote {Path.Combine(outDir, "overrides.json")} (sha256 {hash}).");
return 0;

JsonObject? ReadOverrideFile(string path, string name, List<string> errs)
{
    if (new FileInfo(path).Length > MaxFileBytes) { errs.Add($"{name}: larger than {MaxFileBytes} bytes"); return null; }
    JsonNode? node;
    try
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        if (stream.Documents.Count != 1) { errs.Add($"{name}: must hold exactly one YAML document"); return null; }
        node = ToJson(stream.Documents[0].RootNode);
    }
    catch (YamlException e)
    {
        errs.Add($"{name}: not valid YAML (line {e.Start.Line}, column {e.Start.Column}): {e.Message}");
        return null;
    }
    catch (InvalidDataException e)
    {
        errs.Add($"{name}: {e.Message}");
        return null;
    }

    var result = schema.Evaluate(JsonDocument.Parse(node?.ToJsonString() ?? "null").RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
    if (!result.IsValid)
    {
        foreach (var d in (result.Details ?? []).Where(d => d.Errors is not null))
            foreach (var e in d.Errors!) errs.Add($"{name}: {(d.InstanceLocation.ToString() is { Length: > 0 } loc ? loc : "/")}: {e.Value}");
        return null;
    }

    var obj = node!.AsObject();
    var before = errs.Count;
    foreach (var (entry, i) in obj["entries"]!.AsArray().Select((e, i) => (e!.AsObject(), i + 1)))
    {
        var where = $"{name}: entries[{i}]";
        foreach (var key in new[] { "packageVersions", "umbraco" })
            if (!VersionRange.TryParse(entry[key]!.GetValue<string>(), out _)) errs.Add($"{where}.{key} is not a NuGet version range");
        var verified = Date(entry["verifiedOn"], $"{where}.verifiedOn", errs);
        var reviewBy = Date(entry["reviewBy"], $"{where}.reviewBy", errs);
        if (verified is not null && reviewBy is not null && reviewBy <= verified) errs.Add($"{where}.reviewBy must be after verifiedOn");
        if (entry["replacement"]?.GetValue<string>() is { } replacement
            && string.Equals(replacement, obj["id"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            errs.Add($"{where}.replacement cannot be the package itself");
    }
    return errs.Count == before ? obj : null;
}

static DateOnly? Date(JsonNode? node, string where, List<string> errs)
{
    if (node is null) return null;
    if (DateOnly.TryParseExact(node.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
    errs.Add($"{where} is not a real yyyy-MM-dd date");
    return null;
}

// Every scalar stays a string (so `1.0` or `yes` never turns into a number or boolean); only a plain `null`, `~`
// or empty value becomes null. Anchors and aliases are refused: entries are short and must read on their own.
static JsonNode? ToJson(YamlNode node)
{
    if (!node.Anchor.IsEmpty) throw new InvalidDataException("YAML anchors and aliases are not allowed");
    switch (node)
    {
        case YamlMappingNode map:
            var obj = new JsonObject();
            foreach (var (k, v) in map.Children)
            {
                var key = ((YamlScalarNode)k).Value ?? "";
                if (obj.ContainsKey(key)) throw new InvalidDataException($"duplicate key {key}");
                obj[key] = ToJson(v);
            }
            return obj;
        case YamlSequenceNode seq:
            return new JsonArray(seq.Children.Select(ToJson).ToArray());
        case YamlScalarNode scalar:
            if (scalar.Style == ScalarStyle.Plain && scalar.Value is null or "" or "null" or "~") return null;
            return JsonValue.Create(scalar.Value);
        default:
            throw new InvalidDataException("YAML anchors and aliases are not allowed");
    }
}

static JsonNode? ReadVersionsTable(string path, List<string> errs)
{
    const string name = "data/umbraco-versions.json";
    JsonNode? doc;
    try { doc = JsonNode.Parse(File.ReadAllText(path)); }
    catch (Exception e) when (e is JsonException or IOException) { errs.Add($"{name}: {e.Message}"); return null; }
    if (doc?["majors"] is not JsonArray majors || majors.Count == 0) { errs.Add($"{name}: needs a non-empty majors array"); return null; }
    var seen = new HashSet<int>();
    foreach (var m in majors)
    {
        var ok = m?["major"] is JsonValue mv && mv.TryGetValue<int>(out var major) && major > 0
            && m["lts"] is JsonValue lv && lv.TryGetValue<bool>(out _)
            && m["tfm"] is JsonValue tv && tv.TryGetValue<string>(out var tfm) && tfm.StartsWith("net", StringComparison.Ordinal)
            && m["eol"] is JsonValue ev && ev.TryGetValue<string>(out var eol)
            && DateOnly.TryParseExact(eol, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        if (!ok) { errs.Add($"{name}: every major needs major (int), lts (bool), eol (yyyy-MM-dd) and tfm (netX.Y): {m?.ToJsonString()}"); continue; }
        if (!seen.Add(m!["major"]!.GetValue<int>())) errs.Add($"{name}: major {m["major"]} listed twice");
    }
    return doc;
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "data", "umbraco-versions.json")) && Directory.Exists(Path.Combine(dir.FullName, "overrides")))
            return dir.FullName;
    throw new InvalidOperationException("run from inside the Umbraco.Community.PackageCheck repository");
}
