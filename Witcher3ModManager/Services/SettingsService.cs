using System.IO;
using System.Text.Json;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public AppSettings Current => _settings;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataPath.FolderName);
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
        Load();
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_settings, options);
        Core.Storage.AtomicFile.WriteAllText(_settingsPath, json);
    }

    private void Load()
    {
        // Same recovery as the mod library: a damaged settings file is copied aside and the
        // previous good version is used, so a configured game or tool path is not silently lost.
        var load = Core.Storage.TextFileRecovery.Load(_settingsPath, LooksLikeSettings);
        if (load.Text is not null && TryDeserialize(load.Text, out var settings))
            _settings = settings;
    }

    private static bool LooksLikeSettings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeserialize(string json, out AppSettings settings)
    {
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            return true;
        }
        catch
        {
            settings = new AppSettings();
            return false;
        }
    }

    public void SetBackgroundImage(string path) { _settings.BackgroundImagePath = path; Save(); }
    public void SetGamePath(string path) { _settings.Witcher3GamePath = path; Save(); }
    public void SetSevenZipPath(string path) { _settings.SevenZipPath = path; Save(); }
    public void SetLanguage(string lang) { _settings.Language = lang; Save(); }
    public void SetAutoBackup(bool on) { _settings.AutoBackup = on; Save(); }
    public void SetBackupPath(string path) { _settings.BackupPath = path; Save(); }
    public void SetLaunchArgs(string args) { _settings.LaunchArgs = args; Save(); }
    public void SetScriptMergerPath(string path) { _settings.ScriptMergerPath = path; Save(); }
    public void SetNexusModsUrl(string url) { _settings.NexusModsUrl = url; Save(); }

    public event Action? SettingsChanged;
    public void NotifyChanged() => SettingsChanged?.Invoke();
}
