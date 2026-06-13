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

    public void AddMod(ModEntry mod)
    {
        // Auto-assign priority: highest existing + 1
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
