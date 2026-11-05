namespace PackageCheck.Core;

/// <summary>Facts about Umbraco's history, not target majors (those come from the feed or umbraco-versions.json).</summary>
public static class UmbracoFacts
{
    /// <summary>
    /// The first major with the Lit backoffice (design 7.4, H03 and H08). Ranges whose floor sits below it were
    /// written for the AngularJS backoffice. The only Umbraco major literal allowed in src/ (NoHardCodedMajorTests).
    /// </summary>
    public const int NewBackofficeMajor = 14;

    /// <summary>
    /// Core packages removed with the new backoffice (H05). Only the id design 7.4 names; add one only with a cited line
    /// from the Umbraco 14.0 release notes or breaking-changes page beside it (H05Tests pins the list).
    /// </summary>
    public static IReadOnlyList<string> RemovedBackofficePackages { get; } = ["Umbraco.Cms.Web.BackOffice"];

    /// <summary>The package whose versions and nuspec define a target (design 6.1).</summary>
    public const string CmsPackageId = "Umbraco.Cms";
}
