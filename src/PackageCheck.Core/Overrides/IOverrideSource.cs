namespace PackageCheck.Core.Overrides;

/// <summary>Where overrides come from: the bundled seed, a local file or URL (<c>--overrides</c>), later the community release.</summary>
public interface IOverrideSource
{
    Task<OverrideSet> LoadAsync(CancellationToken ct);
}
