namespace Witcher3ModManager.Core.Analysis.Script;

/// <summary>One component and the script symbols it contributes.</summary>
public sealed record ComponentSymbols(
    string ComponentId,
    IReadOnlyList<ScriptSymbol> Symbols,
    string? Fingerprint = null);

/// <summary>
/// Finds methods that more than one component overrides. The join key is <c>Class::Method</c>, so
/// a compiled blob and a loose script are compared on equal terms — the case file-level comparison
/// misses entirely.
///
/// Only a definition counts. <c>@wrapMethod</c> is composable: the engine chains every wrapper in
/// load order, so two mods wrapping the same method is normal and never a conflict. A blob class
/// function, a <c>@replaceMethod</c> or an added method, on the other hand, replaces the body and
/// collides with any other definition of the same symbol.
/// </summary>
public static class ScriptSymbolConflictAnalyzer
{
    public const string SuggestAction =
        "同一脚本函数被多个 mod 覆盖，Script Merger 无法合并，只能一方生效。";

    public static IReadOnlyList<ConflictEvidence> Analyze(IEnumerable<ComponentSymbols> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var groups = new Dictionary<string, SymbolGroup>(StringComparer.OrdinalIgnoreCase);
        var fingerprints = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component.ComponentId)) continue;

            fingerprints.TryAdd(component.ComponentId, component.Fingerprint);

            var seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // key -> is definition
            foreach (var symbol in component.Symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol.MethodName)) continue;

                var key = symbol.ClassName + "::" + symbol.MethodName;
                var isDefinition = symbol.Kind != ScriptSymbolKind.Wrap;

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new SymbolGroup(symbol.ClassName, symbol.MethodName);
                    groups[key] = group;
                }

                if (isDefinition) group.HasDefinition = true;

                if (seen.TryGetValue(key, out var alreadyDefinition))
                {
                    // The same component may contribute the symbol twice (a blob plus a wrapper,
                    // for example). A definition anywhere in it still counts.
                    if (isDefinition && !alreadyDefinition) seen[key] = true;
                    continue;
                }

                seen[key] = isDefinition;
                group.Components.Add(component.ComponentId);
            }
        }

        return groups.Values
            .Where(group => group.Components.Count >= 2
                            && group.HasDefinition
                            && !AllIdentical(group, fingerprints))
            .OrderBy(group => group.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.MethodName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ConflictEvidence(
                TargetDomain.Mods,
                $"{group.ClassName}::{group.MethodName}",
                group.Components.ToList(),
                ConflictKind.ScriptOverride,
                Array.Empty<string>(),
                SuggestAction))
            .ToList();
    }

    /// <summary>
    /// Two copies of the same scripts are not a conflict: the game ends up with the same
    /// behaviour. Only an unknown fingerprint keeps the finding, because "cannot tell" must not
    /// silently remove a real override.
    /// </summary>
    private static bool AllIdentical(
        SymbolGroup group,
        IReadOnlyDictionary<string, string?> fingerprints)
    {
        var values = group.Components
            .Select(id => fingerprints.TryGetValue(id, out var value) ? value : null)
            .ToList();

        if (values.Any(value => string.IsNullOrEmpty(value))) return false;
        return values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
    }

    private sealed class SymbolGroup
    {
        public SymbolGroup(string className, string methodName)
        {
            ClassName = className;
            MethodName = methodName;
        }

        public string ClassName { get; }

        public string MethodName { get; }

        public SortedSet<string> Components { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when at least one contributor replaces rather than wraps the method.</summary>
        public bool HasDefinition { get; set; }
    }
}
