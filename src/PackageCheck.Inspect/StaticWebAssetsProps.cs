using System.Xml;

namespace PackageCheck.Inspect;

/// <summary>
/// Reads <c>BasePath</c> from a package's <c>Microsoft.AspNetCore.StaticWebAssets.props</c> (S2, ruling R21). The props
/// is package-controlled XML: DTDs are prohibited and no resolver is set, so entities are never expanded and no file or
/// URL is fetched. A bad document is reported to the caller, which turns it into an evidence note.
/// </summary>
public static class StaticWebAssetsProps
{
    // Bytes are already capped by CountingReadStream; this bounds the parser's own work on top.
    private const long MaxCharacters = 10 * 1024 * 1024;

    /// <summary>The first non-empty <c>BasePath</c>, whitespace-trimmed (R21: "/" means web root and is kept as-is).</summary>
    /// <exception cref="XmlException">The document is malformed or declares a DTD.</exception>
    public static string? ReadBasePath(Stream props)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
        using var reader = XmlReader.Create(props, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "BasePath") continue;
            var value = reader.ReadElementContentAsString().Trim();
            if (value.Length > 0) return value;
        }
        return null;
    }
}
