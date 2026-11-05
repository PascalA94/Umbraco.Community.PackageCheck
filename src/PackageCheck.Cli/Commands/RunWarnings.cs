using System.Collections.Concurrent;

namespace PackageCheck.Cli.Commands;

/// <summary>Run-level warnings raised by services during a scan (an unreadable overrides file), collected for the report.</summary>
public sealed class RunWarnings
{
    private readonly ConcurrentQueue<string> _items = new();

    public void Add(string warning) => _items.Enqueue(warning);

    public IReadOnlyList<string> Items => [.. _items];
}
