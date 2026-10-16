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
            AppDataPath.FolderName);
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
            Version = Core.Deployment.PresetSelection.CurrentVersion,
            ModStates = BuildPresetStates(mods)
        };

        var safeName = GetSafeFileName(name);
        var path = Path.Combine(_presetsDir, safeName + ".json");
        var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
        Core.Storage.AtomicFile.WriteAllText(path, json);
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

    /// <summary>
    /// One entry per record, keyed by its id. A package's components share one enabled state, so a
    /// preset has one opinion per package — the per-folder entries the old shape needed only
    /// existed because matching happened by folder name.
    /// </summary>
    private static Dictionary<string, bool> BuildPresetStates(List<ModEntry> mods)
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.Id)) continue;
            states[mod.Id] = mod.IsEnabled;
        }
        return states;
    }

    private static string GetSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "preset" : safe;
    }
}
