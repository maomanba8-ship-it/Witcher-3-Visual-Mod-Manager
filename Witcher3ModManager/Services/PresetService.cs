using System.IO;
using System.Text.Json;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class PresetService
{
    private readonly string _presetsDir;

    public PresetService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Witcher3ModManager");
        _presetsDir = Path.Combine(dataDir, "presets");
        Directory.CreateDirectory(_presetsDir);
    }

    public List<PresetInfo> GetAllPresets()
    {
        var presets = new List<PresetInfo>();
        if (!Directory.Exists(_presetsDir)) return presets;

        foreach (var file in Directory.GetFiles(_presetsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<PresetInfo>(json);
                if (preset != null) presets.Add(preset);
            }
            catch { }
        }
        return presets.OrderByDescending(p => p.CreatedAt).ToList();
    }

    public void SavePreset(string name, List<ModEntry> mods)
    {
        var preset = new PresetInfo
        {
            Name = name,
            CreatedAt = DateTime.Now,
            ModStates = mods.Where(m => !string.IsNullOrEmpty(m.GameModFolderName)).GroupBy(m => m.GameModFolderName!).ToDictionary(g => g.Key, g => g.First().IsEnabled)
        };

        var safeName = GetSafeFileName(name);
        var path = Path.Combine(_presetsDir, safeName + ".json");
        var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public void DeletePreset(string name)
    {
        var safeName = GetSafeFileName(name);
        var path = Path.Combine(_presetsDir, safeName + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    public PresetInfo? GetPreset(string name)
    {
        var safeName = GetSafeFileName(name);
        var path = Path.Combine(_presetsDir, safeName + ".json");
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PresetInfo>(json);
        }
        catch { return null; }
    }

    private static string GetSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "preset" : safe;
    }
}
