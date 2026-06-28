using System.Text.RegularExpressions;
using ProcessAst.Core;

namespace ProcessAst.Bw;

public sealed class XPathXsltNormalizer
{
    public (MappingExpressionKind kind, string normalized) Normalize(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return (MappingExpressionKind.Unknown, string.Empty);
        var e = expression.Trim();
        e = Regex.Replace(e, "\\s+", " ");
        e = e.Replace("\r", " ").Replace("\n", " ").Trim();
        e = Regex.Replace(e, @"\s*/\s*", "/");
        e = Regex.Replace(e, @"\s*\[\s*", "[");
        e = Regex.Replace(e, @"\s*\]\s*", "]");
        e = Regex.Replace(e, @"normalize-space\s*\(", "normalize-space(", RegexOptions.IgnoreCase);
        if (e.Contains("<xsl:", StringComparison.OrdinalIgnoreCase) || e.Contains("xsl:value-of", StringComparison.OrdinalIgnoreCase)) return (MappingExpressionKind.Xslt, e);
        if (e.StartsWith("$", StringComparison.Ordinal)) return (MappingExpressionKind.VariableReference, e);
        if (e.StartsWith("/", StringComparison.Ordinal) || e.Contains("/", StringComparison.Ordinal)) return (MappingExpressionKind.XPath, e);
        if (e.StartsWith("'", StringComparison.Ordinal) || e.StartsWith("\"", StringComparison.Ordinal)) return (MappingExpressionKind.Literal, e);
        return (MappingExpressionKind.Unknown, e);
    }
}
