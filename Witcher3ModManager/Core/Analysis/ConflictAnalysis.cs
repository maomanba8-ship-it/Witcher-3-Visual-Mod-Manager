using System.IO;

namespace Witcher3ModManager.Core.Analysis;

/// <summary>
/// The category of a conflict result. Duplicate content is NOT an error; definite
/// overrides are. Script/bin risks are flagged distinctly.
/// </summary>
public enum ConflictKind
{
    DuplicateContent,   // same domain, same target, same hash -> notice only
    DefiniteOverride,   // same domain, same target, different hash
    ScriptOverride,     // DefiniteOverride + .ws / script rule
    BinOverrideRisk,    // same Bin domain, different hash
    UnsupportedContainer, // bundle / binary that cannot be statically expanded
    CategorySimilarity  // hint only, never a real conflict
}

/// <summary>
/// A single normalized file contributed by a component, used as conflict input.
/// </summary>
public sealed record ComponentFile(
    TargetDomain Domain,
    string ComponentIdentity,
    string NormalizedRelativePath,
    string Sha256,
    long Length);

/// <summary>
/// One evidence-backed conflict group result.
/// </summary>
public sealed record ConflictEvidence(
    TargetDomain Domain,
    string NormalizedRelativePath,
    IReadOnlyList<string> ComponentIdentities,
    IReadOnlyList<string> DisplayNames,
    ConflictKind Kind,
    IReadOnlyList<string> Hashes,
    string? SuggestAction = null);

/// <summary>
/// Pure conflict analyzer. Input: component file manifests. Output: evidence-backed
/// conflict groups grouped by (Domain, NormalizedRelativePath). It NEVER touches the
/// real game directory for ownership inference, NEVER decides a final winner, and
/// NEVER supports a specific mod by name.
/// </summary>
public static class ConflictAnalyzer
{
    /// <summary>
    /// Normalize a path: fixed separators, case-insensitive, strip leading "./".
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var maybeWindows = path.Replace("\\", "/");
        var normalized = maybeWindows.Replace("//", "/");
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimStart('/').ToLowerInvariant();
    }

        public static IReadOnlyList<ConflictEvidence> Analyze(IEnumerable<ComponentFile> files)
    {
        var grouped = files
            .Select(f => f with { NormalizedRelativePath = NormalizePath(f.NormalizedRelativePath) })
            .GroupBy(f => (f.Domain, f.NormalizedRelativePath))
            .OrderBy(g => g.Key.Domain)
            .ThenBy(g => g.Key.NormalizedRelativePath)
            .ToList();

        var results = new List<ConflictEvidence>();
        foreach (var group in grouped)
        {
            var distinct = group.ToList();
            if (distinct.Count <= 1) continue; // no conflict

            var identities = distinct.Select(f => f.ComponentIdentity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var displayNames = distinct.Select(f => f.ComponentIdentity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var hashes = distinct.Select(f => f.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var allSameHash = hashes.Count == 1;

            ConflictKind kind;
            string? action = null;

            if (group.Key.Domain == TargetDomain.Bin)
            {
                kind = allSameHash ? ConflictKind.DuplicateContent : ConflictKind.BinOverrideRisk;
                action = allSameHash ? null : "bin 覆盖存在风险，建议检查游戏启动是否受影响。";
            }
            else if (allSameHash)
            {
                kind = ConflictKind.DuplicateContent;
                action = null;
            }
            else
            {
                var isScript = group.Key.NormalizedRelativePath.EndsWith(".ws", StringComparison.OrdinalIgnoreCase) ||
                               group.Key.NormalizedRelativePath.EndsWith(".es", StringComparison.OrdinalIgnoreCase);
                kind = isScript ? ConflictKind.ScriptOverride : ConflictKind.DefiniteOverride;
                action = isScript ? "存在脚本覆盖，建议运行 Script Merger 处理。" : "同一目标不同内容，存在确定覆盖。";
            }

            results.Add(new ConflictEvidence(
                group.Key.Domain,
                group.Key.NormalizedRelativePath,
                identities,
                displayNames,
                kind,
                hashes,
                action));
        }

        return results;
    }

    public static ConflictKind Classify(TargetDomain domain, IReadOnlyList<string> hashes, string normalizedPath)
    {
        var distinct = hashes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (domain == TargetDomain.Bin)
            return distinct.Count == 1 ? ConflictKind.DuplicateContent : ConflictKind.BinOverrideRisk;

        if (distinct.Count == 1)
            return ConflictKind.DuplicateContent;

        var isScript = normalizedPath.EndsWith(".ws", StringComparison.OrdinalIgnoreCase) ||
                       normalizedPath.EndsWith(".es", StringComparison.OrdinalIgnoreCase);
        return isScript ? ConflictKind.ScriptOverride : ConflictKind.DefiniteOverride;
    }
}
