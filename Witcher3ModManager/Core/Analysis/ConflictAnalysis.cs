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
    GeneratedArtifactDiff, // known auto-generated file differing across mods; informational, not a hard conflict
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
/// One evidence-backed conflict group result. Identities are component ids only: the
/// analysis layer has no storage access, so user-facing display names are resolved by the
/// caller that owns the records.
/// </summary>
public sealed record ConflictEvidence(
    TargetDomain Domain,
    string NormalizedRelativePath,
    IReadOnlyList<string> ComponentIdentities,
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

    private static bool IsOpaqueContainerPath(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath)) return false;
        if (normalizedPath.Contains("bundles/", StringComparison.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(normalizedPath);
        return ext.Equals(".bundle", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".rsblob", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bms", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".pak", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Known WolvenKit auto-generated artifacts that share a fixed path but have no
    /// cross-mod behavioral impact (localization stubs, metadata, caches). A different
    /// hash here is informational, not a hard override. Opaque containers (bundles,
    /// archives) are NOT covered here — they remain UnsupportedContainer.
    /// </summary>
    private static bool IsKnownGeneratedArtifact(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath)) return false;
        if (normalizedPath.EndsWith(".w3strings", StringComparison.OrdinalIgnoreCase)) return true;
        return normalizedPath.Equals("content/metadata.store", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Equals("content/texture.cache", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Equals("content/collision.cache", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Equals("content/shaderdx12.cache", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Equals("content/info.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Documentation is never read by the game. Two mods that each ship a README are not in
    /// conflict — they only happen to share a name — so these files stay out of the conflict
    /// list entirely instead of showing up as a red "content override".
    /// </summary>
    private static bool IsDocumentationPath(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath)) return false;

        var fileName = Path.GetFileName(normalizedPath);
        if (fileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase)) return true;

        var extension = Path.GetExtension(normalizedPath);
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".nfo", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".odt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ConflictEvidence> Analyze(IEnumerable<ComponentFile> files)
    {
        var grouped = files
            .Select(f => f with { NormalizedRelativePath = NormalizePath(f.NormalizedRelativePath) })
            .Where(f => !IsDocumentationPath(f.NormalizedRelativePath))
            .GroupBy(f => (f.Domain, f.NormalizedRelativePath))
            .OrderBy(g => g.Key.Domain)
            .ThenBy(g => g.Key.NormalizedRelativePath)
            .ToList();

        var results = new List<ConflictEvidence>();
        foreach (var group in grouped)
        {
            var distinct = group
                .GroupBy(f => f.ComponentIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (distinct.Count <= 1) continue; // a single component is not a conflict

            var identities = distinct.Select(f => f.ComponentIdentity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var hashes = distinct.Select(f => f.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var allSameHash = hashes.Count == 1;

            ConflictKind kind;
            string? action = null;

            var isOpaqueContainer = IsOpaqueContainerPath(group.Key.NormalizedRelativePath);
            if (isOpaqueContainer)
            {
                kind = allSameHash ? ConflictKind.DuplicateContent : ConflictKind.UnsupportedContainer;
                action = allSameHash ? null
                    : group.Key.NormalizedRelativePath.EndsWith(".rsblob", StringComparison.OrdinalIgnoreCase)
                        ? "预编译脚本 blob 内容不同，是否冲突取决于内部函数是否重叠，建议用 Script Merger 复核。"
                        : "无法静态展开的容器内容存在差异，建议人工确认。";
            }
            else if (group.Key.Domain == TargetDomain.Bin)
            {
                kind = allSameHash ? ConflictKind.DuplicateContent : ConflictKind.BinOverrideRisk;
                action = allSameHash ? null : "bin 覆盖存在风险，建议检查游戏启动是否受影响。";
            }
            else if (allSameHash)
            {
                kind = ConflictKind.DuplicateContent;
                action = null;
            }
            else if (IsKnownGeneratedArtifact(group.Key.NormalizedRelativePath))
            {
                kind = ConflictKind.GeneratedArtifactDiff;
                action = "WolvenKit 自动生成物差异，通常不影响游戏行为，建议忽略。";
            }
            else
            {
                // Loose scripts override by file path. precompiled.rsblob is NOT treated as a
                // script here: it is a container of compiled functions, and two different blobs
                // only conflict when their symbols overlap. A file hash cannot tell that apart,
                // so the blob stays an opaque container (see IsOpaqueContainerPath).
                var isScript = group.Key.NormalizedRelativePath.EndsWith(".ws", StringComparison.OrdinalIgnoreCase) ||
                               group.Key.NormalizedRelativePath.EndsWith(".es", StringComparison.OrdinalIgnoreCase);
                kind = isScript ? ConflictKind.ScriptOverride : ConflictKind.DefiniteOverride;
                action = isScript ? "存在脚本覆盖，建议运行 Script Merger 处理。" : "同一目标不同内容，存在确定覆盖。";
            }

            results.Add(new ConflictEvidence(
                group.Key.Domain,
                group.Key.NormalizedRelativePath,
                identities,
                kind,
                hashes,
                action));
        }

        return results;
    }
}
