namespace PackageCheck.Core.Heuristics;

public enum EvidenceResult { Positive, Negative, Info }

public enum EvidenceWeight { High, Medium, Low, Info }

/// <summary>
/// One line of evidence behind a verdict (design 7.4). <paramref name="Source"/> names where it came from
/// (nuget, marketplace, nupkg, override). <paramref name="Alternate"/> carries a replacement id as data, so nothing
/// ever parses it back out of <paramref name="Detail"/> (ruling R34).
/// </summary>
public sealed record Evidence(string Check, EvidenceResult Result, EvidenceWeight Weight, string Source, string Detail, string? Alternate = null)
{
    public bool IsHighNegative => Result == EvidenceResult.Negative && Weight == EvidenceWeight.High;
    public bool IsHighPositive => Result == EvidenceResult.Positive && Weight == EvidenceWeight.High;
}

public static class EvidenceSources
{
    public const string NuGet = "nuget";
    public const string Marketplace = "marketplace";
    public const string Nupkg = "nupkg";
}
