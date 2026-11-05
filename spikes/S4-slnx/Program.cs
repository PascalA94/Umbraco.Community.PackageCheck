using System.Xml.Linq;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

// Spike S4: compare SolutionPersistence with a hand-rolled reader. Usage: S4 <file>...
var printAll = args.Contains("--all");
foreach (var file in args.Where(a => a != "--all"))
{
    var lib = await ReadWithLibrary(file);
    Console.WriteLine($"{Path.GetFileName(file)}: library={lib.Count} backslashes={lib.Count(p => p.Contains('\\'))} rooted={lib.Count(Path.IsPathRooted)}");
    if (file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
    {
        var xml = ReadWithXDocument(file);
        var same = lib.Select(x => x.Replace('\\', '/')).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(xml.OrderBy(x => x, StringComparer.Ordinal));
        Console.WriteLine($"  xdocument={xml.Count} identical={same}");
        if (!same)
        {
            Console.WriteLine("  only library: " + string.Join(", ", lib.Except(xml)));
            Console.WriteLine("  only xdoc:    " + string.Join(", ", xml.Except(lib)));
        }
    }
    foreach (var p in printAll ? lib : lib.Take(6).ToList()) Console.WriteLine("    " + p);
}

static async Task<List<string>> ReadWithLibrary(string path)
{
    var serializer = SolutionSerializers.GetSerializerByMoniker(path)
        ?? throw new NotSupportedException(path);
    var model = await serializer.OpenAsync(path, CancellationToken.None);
    return model.SolutionProjects.Select(p => p.FilePath).ToList();
}

static List<string> ReadWithXDocument(string path) =>
    XDocument.Load(path).Descendants("Project")
        .Select(p => (string?)p.Attribute("Path"))
        .Where(p => p is not null)
        .Select(p => p!.Replace('\\', '/'))
        .ToList();
