using PackageCheck.Sources.Cache;

// Usage: CacheHammer <cache root> <rounds>. Writes and reads the same 50 keys as a sibling process does, then checks
// every entry it can read parses to the value it expects. Exit 0 = no exception and every read valid.
var cache = new FileCache(args[0], TimeProvider.System);
var rounds = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
for (var r = 0; r < rounds; r++)
{
    for (var i = 0; i < 50; i++)
    {
        var key = new CacheKey("hammer", "https://feed.example/v3/index.json", $"pkg{i}", "1.0.0", null);
        cache.Write(key, new[] { $"value-{i}", new string('x', 64 * 1024) });
        if (cache.TryRead<string[]>(key, null, out var v) && (v.Length != 2 || v[0] != $"value-{i}"))
        {
            Console.Error.WriteLine($"bad value for pkg{i}");
            return 1;
        }
    }
}
// A torn (half-written) entry reads as corrupt: with tmp-then-rename writes a reader never sees one (Review Focus 5).
if (cache.CorruptReads > 0)
{
    Console.Error.WriteLine($"{cache.CorruptReads} corrupt reads");
    return 1;
}
return 0;
