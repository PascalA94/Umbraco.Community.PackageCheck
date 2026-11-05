// M0 gate (plan Task 7, design section 8): wires the S1 (NuGet.Protocol seam), S2 (nupkg paths) and S3 (metadata scan)
// spike code to a hand-coded copy of the design 7.4 truth table and prints COMPUTED verdicts. No override is applied
// (design 8, Pascal 2026-10-30).
//
//   dotnet run --project spikes/M0-golden -- --target 17.7.2            replay from recorded/ (default)
//   dotnet run --project spikes/M0-golden -- --target 17.7.2 --verbose  also print every piece of evidence
//   dotnet run --project spikes/M0-golden -- --target 17.7.2 --record   re-record live metadata into recorded/
//   --nupkg-cache <dir>  where nupkgs are kept (default: $TMPDIR/umbraco-package-check-m0-nupkgs, outside the repo)
//   --json <file>        also write the outcomes as JSON (used to fill golden-packages.json)
using System.Text.Json;
using M0Golden;
using NuGet.Versioning;

var argList = args.ToList();
string? Arg(string name) { var i = argList.IndexOf(name); return i >= 0 && i + 1 < argList.Count ? argList[i + 1] : null; }
var record = argList.Contains("--record");
var verbose = argList.Contains("--verbose");
var targetText = Arg("--target") ?? "17.7.2";
if (!NuGetVersion.TryParse(targetText, out var targetVersion) || targetText.Split('.').Length < 3)
{
    Console.Error.WriteLine("M0 takes an exact --target version, for example 17.7.2");
    return 2;
}

var projectDir = FindProjectDir();
var recordedDir = Path.Combine(projectDir, "recorded");
var httpDir = Path.Combine(recordedDir, "http");
var pinsPath = Path.Combine(recordedDir, "nupkgs.json");
var snapshotInfoPath = Path.Combine(recordedDir, "snapshot.json");
var nupkgCache = Arg("--nupkg-cache") ?? Path.Combine(Path.GetTempPath(), "umbraco-package-check-m0-nupkgs");

if (record && Directory.Exists(httpDir)) Directory.Delete(httpDir, recursive: true);
// NuGet.Protocol keeps an HTTP cache below the seam; a private, throwaway one keeps every request visible (S1).
var nugetHttpCache = Directory.CreateTempSubdirectory("m0-nuget-http-").FullName;
Environment.SetEnvironmentVariable("NUGET_HTTP_CACHE_PATH", nugetHttpCache);
var loadedBefore = LoadedNames();

try
{
    var seam = new SnapshotHandler(httpDir, record);
    var feed = await Feed.CreateAsync(seam);
    var store = new NupkgStore(nupkgCache, pinsPath, record);
    var engine = new Engine(feed, store);

    async Task<Target> TargetFor(NuGetVersion v) => new(v, await feed.TargetTfmAsync(v));
    var target = await TargetFor(targetVersion);
    var recordedOn = record ? DateTime.UtcNow.ToString("yyyy-MM-dd") : (File.Exists(snapshotInfoPath) ? JsonDocument.Parse(File.ReadAllText(snapshotInfoPath)).RootElement.GetProperty("recordedOn").GetString() : "?");

    Console.WriteLine($"M0 gate: target Umbraco.Cms {target.Resolved} ({target.Tfm.GetShortFolderName()}), computed verdicts, no overrides");
    Console.WriteLine($"Metadata: {(record ? "LIVE, recording to" : "replayed from")} spikes/M0-golden/recorded (snapshot {recordedOn}); nupkgs pinned by sha512");
    Console.WriteLine();

    // The four design ACs (section 8) as amended by ruling R19 (Contentment 6.2.1 computes ok), plus the G6 proof line.
    var gate = new List<(LineSpec Line, string Expect, Func<Outcome, bool> Extra, string ExtraText)>
    {
        (new("astroboard", "1.1.3"), "astroboard 1.1.3 -> suspicious", _ => true, ""),
        (new("Umbraco.Community.SimpleDashboards", "17.2.0"), "Umbraco.Community.SimpleDashboards 17.2.0 -> ok",
            o => o.Confidence == "high" && o.Evidence.Any(e => e.Check == "H09" && e.IsHighPositive), "confidence high via H09"),
        (new("Umbraco.Workflow", "17.5.4"), "Umbraco.Workflow 17.5.4 -> ok",
            o => o.Evidence.Any(e => e.Check == "H07" && e.Detail.Contains("Umbraco.Workflow.StaticAssets")), "H07 in Umbraco.Workflow.StaticAssets"),
        (new("Umbraco.Community.Contentment", "6.2.1"), "Umbraco.Community.Contentment 6.2.1 -> ok", _ => true, ""),
        // G6: lowest clearing major, highest patch in it, not 7.x. 5.2.1 is the newest 5.x; its net8.0 range [13.2.0, 14.0.0) excludes 17.
        // R19 fixture (amended to 5.2.1) and R25: no 7.x clears 17.7.2, so HighestCompatible is empty and 7.2.2 is the highest stable.
        (new("Umbraco.Community.Contentment", "5.2.1", Note: "G6, R19 fixture"), "Umbraco.Community.Contentment 5.2.1 -> upgrade 6.3.4",
            o => o.Confidence == "high" && o.HighestCompatible is null && o.LatestStable == "7.2.2" && !o.LatestStableClears,
            "high; HighestCompatible null; highest stable 7.2.2 does not clear"),
    };

    // Step 2 goldens (plan Task 7) and the picks this task makes (reasons in docs/spikes.md, "M0 decisions").
    var step2 = new List<LineSpec>
    {
        new("Crumpled.RobotsTxt", "3.1.1"),
        new("MediaWiz.Forums", "14.0.0"),
        new("Umbraco.Community.Contentment", "4.7.0"),
        new("Umbraco.Community.Contentment", "6.3.3"),
        new("Umbraco.Community.Contentment", "7.2.1"),
        new("Our.Umbraco.Community.Contentment", "4.7.0", Note: "rename"),
        new("Skybrud.Umbraco.Redirects", "13.0.9"),
        new("Skybrud.Umbraco.Redirects", "13.0.9", Prerelease: true),
        new("astroboard", "1.1.3", NoInspect: true),
        new("Plumber.Workflow", "10.1.2", Note: "NuGet-deprecated pick"),
        new("Enterspeed.Source.UmbracoCms.V10", "3.2.0", Note: "Marketplace-retired pick"),
        // H15 (R2): a pair where only a prerelease clears. 13.0.1 is the newest stable and declares [13.0.0, 13.999.0);
        // the only 17.x is 17.0.0-beta001 with [17.0.0, 17.999.0). Same author and shape as the design's Skybrud AC.
        new("Skybrud.Umbraco.Redirects.Import", "13.0.1", Note: "H15 pick"),
        new("Skybrud.Umbraco.Redirects.Import", "13.0.1", Prerelease: true, Note: "H15 pick"),
        // G6 where two package majors clear: Contentment 6.1.x declares [17.0.0, 19.0.0) and 7.x [18.0.0, 19.0.0).
        new("Umbraco.Community.Contentment", "5.2.1", TargetOverride: NuGetVersion.Parse("18.3.0"), Note: "G6, two clearing majors"),
    };

    var results = new List<object>();
    async Task<(Outcome O, string Text)> Run(LineSpec line)
    {
        var t = line.TargetOverride is null ? target : await TargetFor(line.TargetOverride);
        var o = await engine.EvaluateAsync(line, t);
        var text = Format(line, o, t, line.TargetOverride is not null);
        results.Add(new
        {
            id = line.Id, version = line.Version, target = t.Resolved.ToNormalizedString(),
            options = new[] { line.NoInspect ? "--no-inspect" : null, line.Prerelease ? "--prerelease" : null }.Where(x => x is not null),
            note = line.Note, row = o.Row, verdict = o.Verdict, confidence = o.Confidence, reason = o.Reason,
            recommended = o.Recommended, highestCompatible = o.HighestCompatible, latestStable = o.LatestStable,
            prereleaseCompatible = o.PrereleaseCompatible, evidence = o.Evidence.Select(e => e.ToString()),
        });
        return (o, text);
    }

    var failures = new List<string>();
    var checkGate = target.Resolved == NuGetVersion.Parse("17.7.2");
    Console.WriteLine("== Gate (design 8 ACs, R19 amendment, G6) ==");
    foreach (var (line, expect, extra, extraText) in gate)
    {
        var (o, text) = await Run(line);
        Console.WriteLine(text);
        if (verbose) foreach (var e in o.Evidence) Console.WriteLine($"      {e}");
        if (!checkGate) continue;
        if (!text.StartsWith(expect + " ", StringComparison.Ordinal) && text != expect) failures.Add($"expected '{expect}'");
        else if (!extra(o)) failures.Add($"'{expect}': {extraText} not met");
    }

    Console.WriteLine();
    Console.WriteLine("== Step 2 goldens and picks ==");
    foreach (var line in step2)
    {
        var (o, text) = await Run(line);
        Console.WriteLine(text);
        if (verbose) foreach (var e in o.Evidence) Console.WriteLine($"      {e}");
    }

    if (record)
    {
        store.SavePins();
        File.WriteAllText(snapshotInfoPath, JsonSerializer.Serialize(new { recordedOn, userAgent = SnapshotHandler.UserAgent }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
    if (Arg("--json") is { } jsonPath)
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { target = target.Resolved.ToNormalizedString(), recordedOn, results, nupkgs = store.Used.Values }, new JsonSerializerOptions { WriteIndented = true }));

    // S3 Step 3 repeated for this run: no scanned package assembly may appear in the process.
    var loadedAfter = LoadedNames();
    var leaked = loadedAfter.Where(Inspector.ScannedAssemblyNames.Contains).ToList();
    var probe = loadedAfter.Contains("System.Reflection.Metadata");
    Console.WriteLine();
    Console.WriteLine($"No-load check: {Inspector.DllCount} DLLs scanned ({Inspector.ScannedAssemblyNames.Count} names), loaded from packages: {(leaked.Count == 0 ? "none" : string.Join(", ", leaked))}; probe detects System.Reflection.Metadata: {probe}");
    if (leaked.Count > 0 || !probe) failures.Add("no-load check");
    Console.WriteLine($"HTTP: {(record ? $"{seam.RecordedCount} responses recorded" : $"{seam.Served} responses replayed, 0 live metadata calls")}; nupkgs used: {store.Used.Count}");

    if (checkGate)
    {
        Console.WriteLine(failures.Count == 0 ? "GATE: PASS (5 of 5 gate lines match)" : $"GATE: FAIL -> {string.Join("; ", failures)}");
        return failures.Count == 0 ? 0 : 1;
    }
    Console.WriteLine("GATE: not checked (the gate expectations are for --target 17.7.2)");
    return 0;
}
catch (SnapshotMissException e)
{
    Console.Error.WriteLine(e.Message);
    return 3;
}
finally
{
    try { Directory.Delete(nugetHttpCache, recursive: true); } catch (IOException) { }
}

static string Format(LineSpec line, Outcome o, Target t, bool showTarget)
{
    var head = $"{line.Id} {line.Version}";
    if (showTarget) head += $" @ {t.Resolved}";
    if (line.NoInspect) head += " --no-inspect";
    if (line.Prerelease) head += " --prerelease";
    var verdict = o.Verdict switch
    {
        "upgrade" => $"upgrade {o.Recommended}",
        "replaced" => $"replaced by {o.Recommended}",
        _ => o.Verdict,
    };
    var extras = new List<string>();
    if (o.Verdict == "upgrade" && o.LatestStable is not null && o.LatestStable != o.Recommended)
        extras.Add(o.LatestStableClears ? $"highest overall {o.LatestStable}" : $"highest overall {o.LatestStable}, which does not support {t.Resolved}");
    if (o.HighestCompatible is not null && o.HighestCompatible != o.LatestStable) extras.Add($"highest compatible {o.HighestCompatible}");
    if (o.PrereleaseCompatible is not null) extras.Add($"prerelease {o.PrereleaseCompatible} supports it, offered only with --prerelease");
    var paren = extras.Count > 0 ? $" ({string.Join("; ", extras)})" : "";
    var key = o.Evidence.Where(e => e.Weight is "high" || e.Check is "H03" || (e.Check is "H06" && e.Result == "negative"))
        .Select(e => e.Check).Distinct().ToList();
    var meta = $"confidence {o.Confidence ?? "n/a"}; row {o.Row}{(o.Reason is null ? "" : $"; reason {o.Reason}")}{(key.Count > 0 ? $"; {string.Join(", ", key)}" : "")}";
    return $"{head} -> {verdict}{paren}  [{meta}]{(line.Note is null ? "" : $"  ({line.Note})")}";
}

static HashSet<string> LoadedNames() =>
    AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

static string FindProjectDir()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "M0Golden.csproj"))) return d.FullName;
    throw new InvalidOperationException("cannot find spikes/M0-golden");
}
