using System.IO;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class ConflictService
{
    private readonly ModStorageService _storage;
    private readonly Witcher3Service _w3;

    public ConflictService(ModStorageService storage, Witcher3Service w3)
    {
        _storage = storage;
        _w3 = w3;
    }

    /// <summary>
    /// Scan all enabled mods for file conflicts.
    /// Returns a list of ConflictInfo, each representing one conflicting file.
    /// </summary>
    public List<ConflictInfo> DetectConflicts()
    {
        var conflicts = new List<ConflictInfo>();
        var enabledMods = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        if (enabledMods.Count < 2) return conflicts;

        // Build file -> mod mapping from managed mod storage
        // For each enabled mod, scan its InstalledPath for files
        var fileModMap = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var mod in enabledMods)
        {
            if (string.IsNullOrEmpty(mod.InstalledPath) || !Directory.Exists(mod.InstalledPath))
                continue;

            try
            {
                // Guard: skip if path became invalid
                if (!Directory.Exists(mod.InstalledPath)) continue;
                var files = Directory.GetFiles(mod.InstalledPath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    // Get relative path inside the mod folder
                    var relativePath = file[(mod.InstalledPath.Length + 1)..];
                    // Normalize path separators
                    relativePath = relativePath.Replace('\\', '/');

                    if (!fileModMap.ContainsKey(relativePath))
                        fileModMap[relativePath] = new List<string>();
                    
                    if (!fileModMap[relativePath].Contains(mod.Name))
                        fileModMap[relativePath].Add(mod.Name);
                }
            }
            catch { }
        }

        // Also scan game mods directory for overlap
        var gameModsPath = _w3.GetGameModsPath();
        if (gameModsPath != null && Directory.Exists(gameModsPath))
        {
            foreach (var mod in enabledMods)
            {
                if (string.IsNullOrEmpty(mod.GameModFolderName)) continue;
                
                var gameModDir = Path.Combine(gameModsPath, mod.GameModFolderName);
                if (!Directory.Exists(gameModDir)) continue;

                try
                {
                    var files = Directory.GetFiles(gameModDir, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relativePath = file[(gameModDir.Length + 1)..].Replace('\\', '/');
                        
                        if (!fileModMap.ContainsKey(relativePath))
                            fileModMap[relativePath] = new List<string>();
                        
                        if (!fileModMap[relativePath].Contains(mod.Name))
                            fileModMap[relativePath].Add(mod.Name);
                    }
                }
                catch { }
            }
        }

        // Find conflicts: files that appear in 2+ mods
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
    /// Get a summary grouped by mod pairs for display
    /// </summary>
    public List<string> GetConflictSummary()
    {
        var conflicts = DetectConflicts();
        var pairMap = new Dictionary<string, (int Count, List<string> Files)>();

        foreach (var c in conflicts)
        {
            // Sort mod names for consistent keys
            var sortedMods = c.ConflictingMods.OrderBy(x => x).ToList();
            var key = string.Join("|", sortedMods);

            if (!pairMap.ContainsKey(key))
                pairMap[key] = (0, new List<string>());
            
            var existing = pairMap[key];
            pairMap[key] = (existing.Count + 1, existing.Files.Append(c.FilePath).ToList());
        }

        return pairMap.Select(kvp =>
        {
            var mods = kvp.Key.Split('|');
            var filesStr = kvp.Value.Files.Take(3).ToList();
            var more = kvp.Value.Count > 3 ? $" (+{kvp.Value.Count - 3}个文件)" : "";
            return $"{mods[0]} ↔ {mods[1]}: {kvp.Value.Count}个冲突文件\n  {string.Join("\n  ", filesStr)}{more}";
        }).ToList();
    }

    /// <summary>
    /// Check if a specific mod conflicts with any enabled mod
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

