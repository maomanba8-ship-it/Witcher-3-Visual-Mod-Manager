using System.IO;
using System.Text.Json;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class ModStorageService
{
    private readonly string _dataDir;
    private readonly string _modsDir;
    private readonly string _dbPath;
    private List<ModEntry> _mods = new();

    public ModStorageService()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Witcher3ModManager");
        _modsDir = Path.Combine(_dataDir, "mods");
        _dbPath = Path.Combine(_dataDir, "mods.json");
        
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_modsDir);
        Load();
    }

    public IReadOnlyList<ModEntry> GetAllMods() => _mods.AsReadOnly();
    
    public List<ModEntry> GetModsByCategory(CategoryType category)
    {
        var mods = category == CategoryType.All 
            ? _mods.ToList() 
            : _mods.Where(m => m.Category == category).ToList();
        return mods.OrderBy(m => m.Priority).ThenBy(m => m.Name).ToList();
    }

    public ModEntry? GetMod(string id) => _mods.FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// Migrate old multi-entry GroupId data to single-entry HasDlcComponent format.
    /// </summary>
    public void MigrateGroupedEntries()
    {
        var groups = _mods.Where(m => !string.IsNullOrEmpty(m.GroupId))
            .GroupBy(m => m.GroupId)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            var modEntry = group.FirstOrDefault(m => m.ModType == Models.ModType.Mod);
            var dlcEntry = group.FirstOrDefault(m => m.ModType == Models.ModType.Dlc);

            if (modEntry != null && dlcEntry != null)
            {
                modEntry.GameDlcFolderName = dlcEntry.GameModFolderName;
                _mods.Remove(dlcEntry);
            }
        }

        if (groups.Count > 0) Save();
    }

    /// <summary>
    /// Deduplicate entries with the same GameModFolderName — legacy artifacts from:
    ///   - Old Mod+DLC split cards (different GroupId, same folder)
    ///   - Accidental double-imports
    ///   - Incomplete prior migrations
    ///
    /// GameModFolderName is the true unique key (game directory folder name).
    /// UserDisplayName / Name are editable and NOT suitable for dedup.
    ///
    /// Keeps the oldest entry (by ImportDate), merges DlcFolderName
    /// and CoverImagePath from duplicates, then removes the rest.
    /// </summary>
    public void DeduplicateByGameFolderName()
    {
        var dupGroups = _mods
            .Where(m => !string.IsNullOrEmpty(m.GameModFolderName))
            .GroupBy(m => m.GameModFolderName!)
            .Where(g => g.Count() > 1)
            .ToList();

        if (dupGroups.Count == 0) return;

        foreach (var group in dupGroups)
        {
            var sorted = group.OrderBy(m => m.ImportDate).ToList();
            var keeper = sorted[0];

            foreach (var dupe in sorted.Skip(1))
            {
                // Merge DLC info
                if (!string.IsNullOrEmpty(dupe.GameDlcFolderName) &&
                    string.IsNullOrEmpty(keeper.GameDlcFolderName))
                {
                    keeper.GameDlcFolderName = dupe.GameDlcFolderName;
                }

                // Merge cover image
                if (!string.IsNullOrEmpty(dupe.CoverImagePath) &&
                    string.IsNullOrEmpty(keeper.CoverImagePath))
                {
                    keeper.CoverImagePath = dupe.CoverImagePath;
                }

                _mods.Remove(dupe);
            }
        }

        Save();
    }

    /// <summary>
    /// Add a mod entry. Returns false if a mod with the same GameModFolderName
    /// already exists (caller should ask user whether to overwrite).
    /// GameModFolderName is the stable unique key — user-editable Name is not.
    /// </summary>
    public bool AddMod(ModEntry mod)
    {
        if (!string.IsNullOrEmpty(mod.GameModFolderName) &&
            _mods.Any(m => m.GameModFolderName == mod.GameModFolderName))
            return false;

        // Auto-assign priority: highest existing + 1
        mod.Priority = _mods.Count > 0 ? _mods.Max(m => m.Priority) + 1 : 1;
        _mods.Add(mod);
        Save();
        return true;
    }

    /// <summary>
    /// Force-add a mod entry, replacing any existing entry with the same GameModFolderName.
    /// </summary>
    public void AddOrReplaceMod(ModEntry mod)
    {
        if (!string.IsNullOrEmpty(mod.GameModFolderName))
        {
            var existing = _mods.FirstOrDefault(m => m.GameModFolderName == mod.GameModFolderName);
            if (existing != null)
                _mods.Remove(existing);
        }

        mod.Priority = _mods.Count > 0 ? _mods.Max(m => m.Priority) + 1 : 1;
        _mods.Add(mod);
        Save();
    }

    public void RemoveMod(string id)
    {
        var mod = _mods.FirstOrDefault(m => m.Id == id);
        if (mod != null)
        {
            // InstalledPath cleanup handled by Witcher3Service.DeleteMod
            _mods.Remove(mod);
            Save();
        }
    }

    public void UpdateMod(ModEntry mod)
    {
        var existing = _mods.FirstOrDefault(m => m.Id == mod.Id);
        if (existing != null)
        {
            var idx = _mods.IndexOf(existing);
            _mods[idx] = mod;
            Save();
        }
    }

    /// <summary>
    /// Swap priorities of two mods and save.
    /// </summary>
    public void SwapPriorities(ModEntry mod1, ModEntry mod2)
    {
        (mod1.Priority, mod2.Priority) = (mod2.Priority, mod1.Priority);
        UpdateMod(mod1);
        UpdateMod(mod2);
    }

    /// <summary>
    /// Recalculate all priorities (sequential, starting from 1) based on current order.
    /// </summary>
    public void NormalizePriorities()
    {
        var sorted = _mods.OrderBy(m => m.Priority).ThenBy(m => m.ImportDate).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Priority = i + 1;
        }
        _mods = sorted;
        Save();
    }

    public string GetModsDirectory() => _modsDir;

    private void Load()
    {
        if (File.Exists(_dbPath))
        {
            try
            {
                var json = File.ReadAllText(_dbPath);
                _mods = JsonSerializer.Deserialize<List<ModEntry>>(json) ?? new();
            }
            catch { _mods = new(); }
        }
    }

    private void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_mods, options);
        File.WriteAllText(_dbPath, json);
    }
}
