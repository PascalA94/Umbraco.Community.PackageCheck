using NuGet.Versioning;

namespace PackageCheck.Core.Model;

/// <summary>
/// One package a project references. Ids compare with StringComparer.OrdinalIgnoreCase; <see cref="Id"/> is kept as written.
/// <see cref="UnresolvedReason"/> is free text carrying the raw problem (for example the raw "$(UmbracoVersion)" or
/// "17.*"); any non-null value maps to the report reason "unresolved-property" with this text as the evidence detail.
/// </summary>
public sealed record PackageRef(string Id, string? VersionText, NuGetVersion? Version, bool Direct, string? UnresolvedReason);
