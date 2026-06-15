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
        File.WriteAllText(_settingsPath, json);
    }

    private void Load()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
            catch
            {
                _settings = new();
            }
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
