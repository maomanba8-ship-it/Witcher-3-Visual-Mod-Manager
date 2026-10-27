namespace Witcher3ModManager.Core.Analysis.Script;

/// <summary>One component and the script symbols it contributes.</summary>
public sealed record ComponentSymbols(
    string ComponentId,
    IReadOnlyList<ScriptSymbol> Symbols,
    string? Fingerprint = null);

/// <summary>
/// Finds methods that more than one component overrides. The join key is
/// <c>Class::Method</c>, so a compiled blob and a loose script are compared on equal terms — the
/// case file-level comparison misses entirely.
///
/// Two different mechanisms touching the same method cannot be merged by Script Merger; at
/// runtime one of them wins. That is a real conflict, not an informational overlap.
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

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in component.Symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol.MethodName)) continue;

                var key = symbol.ClassName + "::" + symbol.MethodName;
                if (!seen.Add(key)) continue;

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new SymbolGroup(symbol.ClassName, symbol.MethodName);
                    groups[key] = group;
                }

                group.Components.Add(component.ComponentId);
            }
        }

        return groups.Values
            .Where(group => group.Components.Count >= 2 && !AllIdentical(group, fingerprints))
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
    }
}
