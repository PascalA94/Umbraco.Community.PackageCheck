using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace PackageCheck.Sources.Cache;

public sealed record CacheInfo(string Root, int Entries, long Bytes);

/// <summary>
/// A folder of JSON files, one per <see cref="CacheKey"/>, named by the SHA-256 of the normalised key (design 7.5).
/// Safe for interrupted and parallel runs (Review Focus 5): writes go to a unique <c>.tmp</c> file and are renamed
/// into place, so a reader sees a whole old entry or a whole new one; anything that fails to parse is a miss and is
/// overwritten by the next write; <c>.tmp</c> files are never read and are cleaned once older than an hour.
/// The cache is an optimisation only: every IO failure is a miss or a skipped write, never an error.
/// </summary>
public sealed class FileCache(string root, TimeProvider clock)
{
    private static readonly TimeSpan StaleTmpAge = TimeSpan.FromHours(1);
    private int _cleaned;

    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "umbraco-package-check");

    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new NuGetVersionConverter(), new VersionRangeConverter(), new NuGetFrameworkConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Root => root;

    private int _corruptReads;

    /// <summary>Entries that existed but did not parse. Atomic writes keep this at zero under parallel runs (Review Focus 5).</summary>
    public int CorruptReads => Volatile.Read(ref _corruptReads);

    /// <param name="maxAge">Null means any age (offline mode).</param>
    /// <param name="isValid">
    /// Shape check on the parsed value. System.Text.Json fills missing members with null, so valid JSON of the wrong shape
    /// (another tool version, a hand edit) would otherwise be a hit that crashes later (review M3, Q4); it is a miss.
    /// </param>
    public bool TryRead<T>(CacheKey key, TimeSpan? maxAge, out T value, Func<T, bool>? isValid = null)
    {
        value = default!;
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return false;
            var entry = JsonSerializer.Deserialize<Entry<T>>(File.ReadAllBytes(path), Json);
            if (entry is null || entry.Key != key.Normalised || entry.Value is null) return false;
            if (maxAge is { } age && clock.GetUtcNow() - entry.Written > age) return false;
            if (isValid is not null && !isValid(entry.Value))
            {
                Interlocked.Increment(ref _corruptReads);
                return false;
            }
            value = entry.Value;
            return true;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            Interlocked.Increment(ref _corruptReads);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Write<T>(CacheKey key, T value)
    {
        string? tmp = null;
        try
        {
            Directory.CreateDirectory(root);
            if (Interlocked.Exchange(ref _cleaned, 1) == 0) CleanStaleTmp();
            var path = PathFor(key);
            tmp = $"{path[..^".json".Length]}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(new Entry<T>(key.Normalised, clock.GetUtcNow(), value), Json));
            File.Move(tmp, path, overwrite: true);
            tmp = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another process may hold the target open (Windows); losing one write only costs a later fetch.
        }
        finally
        {
            if (tmp is not null) TryDelete(tmp);
        }
    }

    public void Clear()
    {
        if (!Directory.Exists(root)) return;
        foreach (var f in Directory.EnumerateFiles(root, "*.json").Concat(Directory.EnumerateFiles(root, "*.tmp"))) TryDelete(f);
    }

    public CacheInfo Info()
    {
        if (!Directory.Exists(root)) return new CacheInfo(root, 0, 0);
        var files = new DirectoryInfo(root).GetFiles("*.json");
        return new CacheInfo(root, files.Length, files.Sum(f => f.Length));
    }

    private string PathFor(CacheKey key) =>
        Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Normalised))).ToLowerInvariant() + ".json");

    // Leftovers from killed runs. A .tmp younger than an hour may belong to a run still writing it, so it is kept.
    private void CleanStaleTmp()
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - StaleTmpAge;
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*.tmp"))
            if (f.LastWriteTimeUtc < cutoff) TryDelete(f.FullName);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private sealed record Entry<T>(string Key, DateTimeOffset Written, T Value);

    private sealed class NuGetVersionConverter : JsonConverter<NuGetVersion>
    {
        public override NuGetVersion Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            NuGetVersion.TryParse(r.GetString(), out var v) ? v : throw new JsonException("bad version");
        public override void Write(Utf8JsonWriter w, NuGetVersion v, JsonSerializerOptions o) => w.WriteStringValue(v.ToFullString());
    }

    private sealed class VersionRangeConverter : JsonConverter<VersionRange>
    {
        public override VersionRange Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            VersionRange.TryParse(r.GetString() ?? "", out var v) ? v : throw new JsonException("bad range");
        public override void Write(Utf8JsonWriter w, VersionRange v, JsonSerializerOptions o) => w.WriteStringValue(v.ToNormalizedString());
    }

    private sealed class NuGetFrameworkConverter : JsonConverter<NuGetFramework>
    {
        public override NuGetFramework Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            NuGetFramework.ParseFolder(r.GetString() ?? throw new JsonException("bad framework"));
        public override void Write(Utf8JsonWriter w, NuGetFramework v, JsonSerializerOptions o) => w.WriteStringValue(v.GetShortFolderName());
    }
}
