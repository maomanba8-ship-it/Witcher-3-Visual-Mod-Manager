using System.IO;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class ConflictService
{
    private readonly ModStorageService _storage;
    private readonly Witcher3Service _w3;
    private List<ConflictInfo>? _cachedConflicts;
    private bool _cacheValid;

    public ConflictService(ModStorageService storage, Witcher3Service w3)
    {
        _storage = storage;
        _w3 = w3;
    }

    /// <summary>
    /// Invalidate cache so next DetectConflicts() does a fresh scan.
    /// Call after: import, enable, disable, delete.
    /// </summary>
    public void InvalidateCache()
    {
        _cacheValid = false;
        _cachedConflicts = null;
    }

    /// <summary>
    /// Get conflicts (cached if valid, otherwise full scan).
    /// </summary>
    public List<ConflictInfo> DetectConflicts()
    {
        if (_cacheValid && _cachedConflicts != null)
            return _cachedConflicts;

        _cachedConflicts = ScanGameDirectory();
        _cacheValid = true;
        return _cachedConflicts;
    }

    /// <summary>
    /// Force a fresh scan (used by manual "conflict check" button).
    /// </summary>
    public List<ConflictInfo> ForceDetectConflicts()
    {
        InvalidateCache();
        return DetectConflicts();
    }

    /// <summary>
    /// Scan ONLY Game/mods/ and Game/dlc/ as the single source of truth.
    /// </summary>
    // Engine cache files that modders accidentally bundle — not real conflicts.
    private static readonly HashSet<string> IgnoredPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        @"content\blob0.bundle",
        @"content\buffers0.bundle",
        @"content\collision.cache",
        @"content\texture.cache",
        @"content\metadata.store"
    };

    private List<ConflictInfo> ScanGameDirectory()
    {
        var conflicts = new List<ConflictInfo>();
        var enabledMods = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        if (enabledMods.Count < 2) return conflicts;

        var gamePath = _w3.GetGamePath();
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            return conflicts;

        // Build file -> mod names mapping
        var fileModMap = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        // Scan both mods/ and dlc/ directories
        var rootDirs = new[] { "mods", "dlc" };

        foreach (var rootDir in rootDirs)
        {
            var scanRoot = Path.Combine(gamePath, rootDir);
            if (!Directory.Exists(scanRoot)) continue;

            foreach (var mod in enabledMods)
            {
                if (string.IsNullOrEmpty(mod.GameModFolderName)) continue;

                var modDir = Path.Combine(scanRoot, mod.GameModFolderName);
                if (!Directory.Exists(modDir)) continue;

                try
                {
                    var files = Directory.GetFiles(modDir, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relativePath = file[(modDir.Length + 1)..].Replace('\\', '/');

                                // Skip known engine cache files
                                var normalized = relativePath.Replace('/', '\\');
                                if (IgnoredPaths.Contains(normalized)) continue;

                        if (!fileModMap.ContainsKey(relativePath))
                            fileModMap[relativePath] = new List<string>();

                        if (!fileModMap[relativePath].Contains(mod.Name))
                            fileModMap[relativePath].Add(mod.Name);
                    }
                }
                catch { /* skip inaccessible mod folders */ }
            }
        }

        // Find files owned by 2+ mods → conflict
        foreach (var kvp in fileModMap)
        {
            if (kvp.Value.Count >= 2)
            {
                var isScript = kvp.Key.Contains("scripts/", StringComparison.OrdinalIgnoreCase) ||
                               kvp.Key.EndsWith(".ws", StringComparison.OrdinalIgnoreCase);

                conflicts.Add(new ConflictInfo
                {
                    FilePath = kvp.Key,
                    ConflictingMods = kvp.Value,
                    IsScriptConflict = isScript
                });
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Count potential overrides: mod pairs sharing the same category (excl. All/Other).
    /// Does NOT scan files — purely category-based.
    /// </summary>
    public int DetectPotentialOverrideCount()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        return (int)enabled
            .GroupBy(m => m.Category)
            .Where(g => g.Key != CategoryType.All && g.Key != CategoryType.Other && g.Count() >= 2)
            .Sum(g => (long)g.Count() * (g.Count() - 1) / 2);
    }

    /// <summary>
    /// Get potential override groups keyed by category.
    /// </summary>
    public Dictionary<CategoryType, List<string>> GetPotentialOverrides()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        return enabled
            .GroupBy(m => m.Category)
            .Where(g => g.Key != CategoryType.All && g.Key != CategoryType.Other && g.Count() >= 2)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Name).ToList());
    }

    /// <summary>
    /// Check if a specific mod has potential overrides (same category, 2+ enabled).
    /// </summary>
    public bool HasPotentialOverride(ModEntry mod)
    {
        if (!mod.IsEnabled) return false;
        if (mod.Category == CategoryType.All || mod.Category == CategoryType.Other) return false;
        return _storage.GetAllMods()
            .Count(m => m.IsEnabled && m.Id != mod.Id && m.Category == mod.Category) >= 1;
    }

    /// <summary>
    /// Get a summary grouped by mod pairs for display.
    /// </summary>
    public List<string> GetConflictSummary()
    {
        var conflicts = DetectConflicts();
        var pairMap = new Dictionary<string, (int Count, List<string> Files)>();

        foreach (var c in conflicts)
        {
            var sortedMods = c.ConflictingMods.OrderBy(x => x).ToList();
            var key = string.Join("|", sortedMods);

            if (!pairMap.ContainsKey(key))
                pairMap[key] = (0, new List<string>());

            var existing = pairMap[key];
            pairMap[key] = (existing.Count + 1,
                existing.Files.Append(c.FilePath).ToList());
        }

        return pairMap.Select(kvp =>
        {
            var mods = kvp.Key.Split('|');
            var filesStr = kvp.Value.Files.Take(3).ToList();
            var more = kvp.Value.Count > 3 ? $" (+{kvp.Value.Count - 3}个文件)" : "";
            return $"{mods[0]} \u2194 {mods[1]}: {kvp.Value.Count}个冲突文件\n  {string.Join("\n  ", filesStr)}{more}";
        }).ToList();
    }

    /// <summary>
    /// Check if a specific mod conflicts with any enabled mod.
    /// </summary>
    public List<string> CheckModConflicts(ModEntry mod)
    {
        if (!mod.IsEnabled) return new();

        var conflicts = DetectConflicts();
        return conflicts
            .Where(c => c.ConflictingMods.Contains(mod.Name) && c.ConflictingMods.Count >= 2)
            .Select(c => c.ConflictingMods.FirstOrDefault(m => m != mod.Name) ?? "?")
            .Distinct()
            .ToList();
    }
}