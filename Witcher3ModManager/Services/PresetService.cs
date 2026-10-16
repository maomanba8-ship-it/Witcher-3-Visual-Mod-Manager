using System.IO;
using System.Text.Json;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class PresetService
{
    private readonly string _presetsDir;

    /// <summary>
    /// Preset files that could not be read, and where a copy of each was kept. A preset that
    /// silently disappears from the list is worse than one that fails loudly: the user would have
    /// no way to tell it was ever there.
    /// </summary>
    public IReadOnlyList<string> DamagedFiles => _damagedFiles;
    private readonly List<string> _damagedFiles = new();

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
        _damagedFiles.Clear();
        if (!Directory.Exists(_presetsDir)) return presets;

        foreach (var file in Directory.GetFiles(_presetsDir, "*.json"))
        {
            // Reads through the recovery helper: a damaged file is copied aside first and the
            // previous good version (.bak) is used when there is one.
            var load = Core.Storage.TextFileRecovery.Load(file, LooksLikeAPreset);
            if (load.Text is null)
            {
                if (load.Outcome != Core.Storage.TextFileOutcome.Missing)
                    _damagedFiles.Add(load.Detail ?? file);
                continue;
            }

            var preset = Deserialize(load.Text);
            if (preset is not null) presets.Add(preset);
        }
        return presets.OrderByDescending(p => p.CreatedAt).ToList();
    }

    private static bool LooksLikeAPreset(string json) => Deserialize(json) is not null;

    private static PresetInfo? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PresetInfo>(json);
        }
        catch
        {
            return null;
        }
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
