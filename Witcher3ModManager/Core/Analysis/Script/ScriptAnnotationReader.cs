using System.Text.RegularExpressions;

namespace Witcher3ModManager.Core.Analysis.Script;

/// <summary>
/// Extracts the symbols a loose <c>.ws</c> file touches. Mod scripts override the game's classes
/// through annotations rather than by defining whole classes, so the annotations are the symbol
/// table:
///
/// <code>
/// @wrapMethod( W3DamageManagerProcessor ) function ProcessActionReaction( ... )
/// @addMethod( CR4Player ) function CSR_RememberSettings()
/// @replaceMethod( W3DamageManagerProcessor ) function ProcessAction( ... )
/// @addField( CR4Player ) var csrMenuHaze : bool;
/// </code>
///
/// Only annotations are read. A script that redefines a whole class without annotations is not
/// covered yet; it would need a real AngelScript parser, not a regex.
/// </summary>
public static partial class ScriptAnnotationReader
{
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(
        @"@(?<kind>wrapMethod|addMethod|replaceMethod|addField)\s*\(\s*(?<class>[A-Za-z_][A-Za-z0-9_]*)\s*\)\s*(?:(?:function)\s+(?<method>[A-Za-z_][A-Za-z0-9_]*)|(?:var)\s+(?<field>[A-Za-z_][A-Za-z0-9_]*))")]
    private static partial Regex Annotation();

    public static IReadOnlyList<ScriptSymbol> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ScriptSymbol>();

        var source = StripComments(text);
        var seen = new HashSet<(ScriptSymbolKind Kind, string Class, string Method)>();
        var symbols = new List<ScriptSymbol>();

        foreach (Match match in Annotation().Matches(source))
        {
            var kind = match.Groups["kind"].Value switch
            {
                "wrapMethod" => ScriptSymbolKind.Wrap,
                "addMethod" => ScriptSymbolKind.Add,
                "replaceMethod" => ScriptSymbolKind.Replace,
                "addField" => ScriptSymbolKind.AddProperty,
                _ => (ScriptSymbolKind?)null
            };
            if (kind is null) continue;

            var className = match.Groups["class"].Value;
            var methodName = match.Groups["method"].Success
                ? match.Groups["method"].Value
                : match.Groups["field"].Value;
            if (className.Length == 0 || methodName.Length == 0) continue;

            if (!seen.Add((kind.Value, className, methodName))) continue;

            symbols.Add(new ScriptSymbol(
                className,
                methodName,
                kind.Value,
                ScriptSymbolOrigin.LooseScript));
        }

        return symbols;
    }

    private static string StripComments(string text)
    {
        var withoutBlocks = BlockComment().Replace(text, " ");
        return LineComment().Replace(withoutBlocks, "");
    }
}
