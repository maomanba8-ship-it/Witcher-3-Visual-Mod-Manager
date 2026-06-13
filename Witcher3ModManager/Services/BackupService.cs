using System.IO;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

/// <summary>
/// Manages backup of mod files. For Witcher 3, mods are stored in [Witcher3]\mods\.
/// Backup backs up the mod files from local storage to a safe location.
/// </summary>
public class BackupService
{
    private readonly Witcher3Service _w3;
    private readonly SettingsService _settings;
    private string? _backupRoot;

    public BackupService(Witcher3Service w3, SettingsService settings)
    {
        _w3 = w3;
        _settings = settings;
    }

    public string GetBackupRoot()
    {
        if (_backupRoot != null) return _backupRoot;

        var configured = _settings.Current.BackupPath;
        if (!string.IsNullOrEmpty(configured))
        {
            _backupRoot = configured;
            return _backupRoot;
        }

        _backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Witcher3ModManager", "Backups");
        return _backupRoot;
    }

    /// <summary>
    /// Backup a mod's local files (from AppData storage).
    /// </summary>
    public BackupResult BackupModFiles(ModEntry mod)
    {
        var result = new BackupResult();
        if (string.IsNullOrEmpty(mod.InstalledPath) || !Directory.Exists(mod.InstalledPath))
            return result;

        var backupRoot = GetBackupRoot();
        var modBackupDir = Path.Combine(backupRoot, "Mods", mod.Name);

        try
        {
            CopyDirectoryRecursive(mod.InstalledPath, modBackupDir);
            var files = Directory.GetFiles(modBackupDir, "*.*", SearchOption.AllDirectories);
            result.BackedUpCount = files.Length;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"备份 {mod.Name} 失败: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Backup all currently enabled mods at once.
    /// </summary>
    public List<BackupResult> BackupAllEnabledMods(List<ModEntry> mods)
    {
        var results = new List<BackupResult>();
        foreach (var mod in mods.Where(m => m.IsEnabled))
            results.Add(BackupModFiles(mod));
        return results;
    }

    /// <summary>
    /// Uninstall all mods from game and local storage.
    /// </summary>
    public UninstallAllResult UninstallAllMods(List<ModEntry> mods, bool keepLocalFiles = false)
    {
        var result = new UninstallAllResult();

        foreach (var mod in mods)
        {
            if (!mod.IsEnabled) continue;

            try
            {
                // Backup first
                if (_settings.Current.AutoBackup)
                    BackupModFiles(mod);

                // Disable in game (rename to ~ prefix in mods folder)
                _w3.DisableMod(mod);

                mod.IsEnabled = false;
                // keep GameModFolderName intact
                result.UninstalledMods.Add(mod.Name);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{mod.Name}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Get backup size info for display.
    /// </summary>
    public (int ModCount, long TotalSize) GetBackupStats()
    {
        var backupRoot = GetBackupRoot();
        var modsDir = Path.Combine(backupRoot, "Mods");
        if (!Directory.Exists(modsDir)) return (0, 0);

        var modDirs = Directory.GetDirectories(modsDir);
        long totalSize = 0;
        foreach (var dir in modDirs)
        {
            try
            {
                totalSize += Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }
            catch { }
        }
        return (modDirs.Length, totalSize);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, dest);
        }
    }
}

public class BackupResult
{
    public int BackedUpCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public int FilesBackedUp => BackedUpCount;
    public bool Success => Errors.Count == 0;
}

public class UninstallAllResult
{
    public List<string> UninstalledMods { get; set; } = new();
    public List<string> RestoredFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;
}
