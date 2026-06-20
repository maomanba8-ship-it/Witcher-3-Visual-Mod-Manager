using System.IO;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class ConflictService
{
    private readonly ModStorageService _storage;
    private readonly Witcher3Service _w3;
    private List<ConflictInfo>? _cachedConflicts;
    private Dictionary<string, List<string>> _sharedBinOverlaps = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheValid;

    public ConflictService(ModStorageService storage, Witcher3Service w3)
    {
        _storage = storage;
        _w3 = w3;
    }

    public IReadOnlyDictionary<string, List<string>> GetSharedBinOverlaps() => _sharedBinOverlaps;

    public void InvalidateCache()
    {
        _cacheValid = false;
        _cachedConflicts = null;
    }

    public List<ConflictInfo> DetectConflicts()
    {
        if (_cacheValid && _cachedConflicts != null)
            return _cachedConflicts;

        _cachedConflicts = ScanGameDirectory();
        _cacheValid = true;
        return _cachedConflicts;
    }

    public List<ConflictInfo> ForceDetectConflicts()
    {
        InvalidateCache();
        return DetectConflicts();
    }

    // ���� Ignore Rule System ��������������������������������������������������������������������������������
    //
    // Admission criteria (ALL three must be met):
    //   1. Present in nearly every WolvenKit-packed mod
    //   2. Overwriting it does NOT change game behavior
    //   3. It provides ZERO diagnostic value for real conflicts
    //
    // ? If a file only meets 1-2 criteria �� do NOT add it here.

    public static IReadOnlyList<IgnoredConflictRule> IgnoredConflictRules { get; } = new List<IgnoredConflictRule>
    {
        new(@"content\blob0.bundle",          "Standard WolvenKit content bundle"),
        new(@"content\buffers0.bundle",       "WolvenKit buffer bundle"),
        new(@"content\collision.cache",       "Auto-generated collision cache"),
        new(@"content\texture.cache",         "Auto-generated texture cache"),
        new(@"content\metadata.store",        "WolvenKit metadata store"),
        new(@"content\info.json",             "WolvenKit auto-generated mod metadata"),
        new(@"content\shaderDx12.cache",      "Auto-generated shader cache (DX12)"),
        new(@"content\*.w3strings",           "WolvenKit auto-generated localization stubs (per-folder, no cross-mod impact)")
    };

    /// <summary>
    /// Fast-path check: is this relative path ignorable?
    /// Uses a switch expression to avoid allocation.
    /// </summary>
    public static bool IsIgnorableConflict(string relativePath)
    {
        var normalized = relativePath.Replace('/', '\\').ToLowerInvariant();

        return normalized switch
        {
            @"content\blob0.bundle"      => true,
            @"content\buffers0.bundle"   => true,
            @"content\collision.cache"   => true,
            @"content\texture.cache"     => true,
            @"content\metadata.store"    => true,
            @"content\info.json"         => true,
            @"content\shaderdx12.cache"  => true,
            _ => normalized.EndsWith(".w3strings")
        };
    }

    /// <summary>
    /// Return the reason a path was ignored, or null if not ignored.
    /// </summary>
    public static string? GetIgnoreReason(string relativePath)
    {
        var normalized = relativePath.Replace('/', '\\');
        return IgnoredConflictRules
            .FirstOrDefault(r => string.Equals(r.Pattern, normalized, StringComparison.OrdinalIgnoreCase))
            ?.Reason;
    }

    // ���� Scanning ����������������������������������������������������������������������������������������������������

    private List<ConflictInfo> ScanGameDirectory()
    {
        var conflicts = new List<ConflictInfo>();
        var enabledMods = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        if (enabledMods.Count < 2) return conflicts;

        var gamePath = _w3.GetGamePath();
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            return conflicts;

        var fileModMap = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        // Scan mod folders
        var modsRoot = Path.Combine(gamePath, "mods");
        if (Directory.Exists(modsRoot))
        {
            foreach (var mod in enabledMods)
            {
                foreach (var folderName in mod.GameModFolders)
                {
                    if (string.IsNullOrEmpty(folderName)) continue;
                    var modDir = Path.Combine(modsRoot, folderName);
                    if (!Directory.Exists(modDir)) continue;

                    try
                    {
                        var files = Directory.GetFiles(modDir, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            var relativePath = file[(modDir.Length + 1)..].Replace('\\', '/');
                            if (IsIgnorableConflict(relativePath)) continue;
                            if (!fileModMap.ContainsKey(relativePath))
                                fileModMap[relativePath] = new List<string>();
                            if (!fileModMap[relativePath].Contains(mod.Name))
                                fileModMap[relativePath].Add(mod.Name);
                        }
                    }
                    catch { }
                }
            }
        }

        // Scan DLC folders
        var dlcRoot = Path.Combine(gamePath, "dlc");
        if (Directory.Exists(dlcRoot))
        {
            foreach (var mod in enabledMods)
            {
                foreach (var dlcFolderName in mod.GameDlcFolders)
                {
                    if (string.IsNullOrEmpty(dlcFolderName)) continue;
                    var dlcDir = Path.Combine(dlcRoot, dlcFolderName);
                    if (!Directory.Exists(dlcDir)) continue;

                    try
                    {
                        var dlcFiles = Directory.GetFiles(dlcDir, "*.*", SearchOption.AllDirectories);
                        foreach (var dlcFile in dlcFiles)
                        {
                            var dlcRelPath = dlcFile.Substring(dlcDir.Length + 1).Replace((char)92, (char)47);
                            if (IsIgnorableConflict(dlcRelPath)) continue;
                            if (!fileModMap.ContainsKey(dlcRelPath))
                                fileModMap[dlcRelPath] = new List<string>();
                            if (!fileModMap[dlcRelPath].Contains(mod.Name))
                                fileModMap[dlcRelPath].Add(mod.Name);
                        }
                    }
                    catch { }
                }
            }
        }

        // Scan bin files — collect shared overlaps (informational, not conflicts)
        var sharedBinFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(Path.Combine(gamePath, "bin")))
        {
            var binModMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in enabledMods)
            {
                if (mod.GameBinFiles.Count == 0) continue;
                foreach (var bf in mod.GameBinFiles)
                {
                    if (binModMap.ContainsKey(bf))
                    {
                        var otherMod = binModMap[bf];
                        if (!sharedBinFiles.ContainsKey(bf))
                            sharedBinFiles[bf] = new List<string> { otherMod };
                        if (!sharedBinFiles[bf].Contains(mod.Name))
                            sharedBinFiles[bf].Add(mod.Name);
                    }
                    else
                    {
                        binModMap[bf] = mod.Name;
                    }
                }
            }
        }
        _sharedBinOverlaps = sharedBinFiles;
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

    // ���� Potential Override ��������������������������������������������������������������������������������

    public int DetectPotentialOverrideCount()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        return (int)enabled
            .GroupBy(m => m.Category)
            .Where(g => g.Key != CategoryType.All && g.Key != CategoryType.Other && g.Count() >= 2)
            .Sum(g => (long)g.Count() * (g.Count() - 1) / 2);
    }

    public Dictionary<CategoryType, List<string>> GetPotentialOverrides()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        return enabled
            .GroupBy(m => m.Category)
            .Where(g => g.Key != CategoryType.All && g.Key != CategoryType.Other && g.Count() >= 2)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Name).ToList());
    }

    public bool HasPotentialOverride(ModEntry mod)
    {
        if (!mod.IsEnabled) return false;
        if (mod.Category == CategoryType.All || mod.Category == CategoryType.Other) return false;
        return _storage.GetAllMods()
            .Count(m => m.IsEnabled && m.Id != mod.Id && m.Category == mod.Category) >= 1;
    }

    // ���� Summaries & Queries ������������������������������������������������������������������������������

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
            var more = kvp.Value.Count > 3 ? $" (+{kvp.Value.Count - 3} more files)" : "";
            return $"{mods[0]} <-> {mods[1]}: {kvp.Value.Count} conflicting files\n  {string.Join("\n  ", filesStr)}{more}";
        }).ToList();
    }

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
