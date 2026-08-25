using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class Witcher3Service
{
    private readonly SettingsService _settings; private readonly ModStorageService? _storage;
    private string? _gamePath;
    private bool _isLaunching;

    public Witcher3Service(SettingsService settings, ModStorageService? storage = null)
    {
        _settings = settings; _storage = storage;
    }

    public string? GetGamePath()
    {
        if (!string.IsNullOrEmpty(_gamePath)) return _gamePath;

        var configured = _settings.Current.Witcher3GamePath;
        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
        {
            var normalized = configured.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalized.EndsWith(Path.Combine("bin", "x64_dx12"), StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(Path.Combine("bin", "x64"), StringComparison.OrdinalIgnoreCase))
            {
                normalized = Path.GetDirectoryName(Path.GetDirectoryName(normalized)) ?? normalized;
            }
            if (Directory.Exists(Path.Combine(normalized, "content")) ||
                File.Exists(Path.Combine(normalized, "bin", "x64", "witcher3.exe")) ||
                File.Exists(Path.Combine(normalized, "bin", "x64_dx12", "witcher3.exe")))
            {
                _gamePath = normalized;
                if (normalized != configured)
                    _settings.SetGamePath(normalized);
                return _gamePath;
            }
        }

        var steamPath = DetectSteamPath();
        if (steamPath != null) { _gamePath = steamPath; _settings.SetGamePath(steamPath); return _gamePath; }

        var gogPath = DetectGogPath();
        if (gogPath != null) { _gamePath = gogPath; _settings.SetGamePath(gogPath); return _gamePath; }

        var commonPaths = new[]
        {
            @"Z:\Steam\steamapps\common\The Witcher 3",
            @"D:\Steam\steamapps\common\The Witcher 3",
            @"C:\Program Files (x86)\Steam\steamapps\common\The Witcher 3",
        };
        foreach (var p in commonPaths)
        {
            if (Directory.Exists(p) && File.Exists(Path.Combine(p, "bin", "x64", "witcher3.exe")))
            { _gamePath = p; _settings.SetGamePath(p); return _gamePath; }
        }
        return null;
    }

    private static string? DetectSteamPath()
    {
        try { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 292030"); var p = key?.GetValue("InstallLocation") as string; if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p; } catch { }
        try { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 292030"); var p = key?.GetValue("InstallLocation") as string; if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p; } catch { }
        return null;
    }

    private static string? DetectGogPath()
    {
        try { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GOG.com\Games\1207664643"); var p = key?.GetValue("PATH") as string; if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p; } catch { }
        try { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games\1207664643"); var p = key?.GetValue("PATH") as string; if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p; } catch { }
        return null;
    }

    public string? GetModsSettingsPath()
    {
        return ComputeModsSettingsPath();
    }

    private string? ComputeModsSettingsPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "The Witcher 3", "mods.settings");
    }

    public string GetGameModsPath()
    {
        var gamePath = GetGamePath();
        if (gamePath == null) throw new InvalidOperationException("Game path not found");
        var p = Path.Combine(gamePath, "mods");
        Directory.CreateDirectory(p);
        return p;
    }

    public string GetGameDlcPath()
    {
        var gamePath = GetGamePath();
        if (gamePath == null) throw new InvalidOperationException("Game path not found");
        return Path.Combine(gamePath, "dlc");
    }
    public bool EnableMod(ModEntry mod, string? storageModsDir = null)
    {
        try
        {
            var gameModsPath = GetGameModsPath();
            var storageDir = _storage?.GetModsDirectory() ?? System.IO.Path.GetDirectoryName(mod.InstalledPath) ?? "";

            // Enable each mod folder
            foreach (var folder in mod.GameModFolders)
            {
                var srcInStorage = System.IO.Path.Combine(storageDir, folder);
                var enabledPath = System.IO.Path.Combine(gameModsPath, folder);
                var disabledPath = System.IO.Path.Combine(gameModsPath, "~" + folder);

                if (Directory.Exists(disabledPath))
                {
                    if (Directory.Exists(enabledPath)) Directory.Delete(enabledPath, true);
                    Directory.Move(disabledPath, enabledPath);
                }
                else if (Directory.Exists(srcInStorage))
                {
                    if (!Directory.Exists(enabledPath))
                        CopyDirectoryRecursive(srcInStorage, enabledPath);
                }
            }

            // Update InstalledPath to game dir (primary folder)
            if (mod.GameModFolders.Count > 0)
                mod.InstalledPath = System.IO.Path.Combine(gameModsPath, mod.GameModFolders[0]);

            // Enable each DLC folder
            if (mod.GameDlcFolders.Count > 0)
            {
                var gameDlcPath = GetGameDlcPath();
                foreach (var dlcFolder in mod.GameDlcFolders)
                {
                    var srcInStorage = System.IO.Path.Combine(storageDir, dlcFolder);
                    var dlcEnabled = System.IO.Path.Combine(gameDlcPath, dlcFolder);
                    var dlcDisabled = System.IO.Path.Combine(gameDlcPath, "~" + dlcFolder);

                    if (Directory.Exists(dlcDisabled))
                    {
                        if (Directory.Exists(dlcEnabled)) Directory.Delete(dlcEnabled, true);
                        Directory.Move(dlcDisabled, dlcEnabled);
                    }
                    else if (!Directory.Exists(dlcEnabled) && Directory.Exists(srcInStorage))
                    {
                        CopyDirectoryRecursive(srcInStorage, dlcEnabled);
                    }
                }
            }


            // Enable bin files
            EnableBinFiles(mod);

            // Update mods.settings priorities
            if (mod.ModType == ModType.Mod)
            {
                var currentMods = GetEnabledModsFromSettings();
                int finalPriority = mod.Priority > 0 ? mod.Priority :
                    (currentMods.Count > 0 ? currentMods.Max(m => m.Priority) + 1 : 1);
                foreach (var folder in mod.GameModFolders)
                {
                    currentMods.RemoveAll(m => m.ModName == folder);
                    currentMods.Add((folder, finalPriority));
                }
                WriteModsSettings(currentMods);
            }

            return true;
        }
        catch { return false; }
    }

    public bool DisableMod(ModEntry mod, string? storageModsDir = null)
    {
        try
        {
            var gameModsPath = GetGameModsPath();

            // Disable each mod folder
            foreach (var folder in mod.GameModFolders)
            {
                if (IsSharedFolder(folder, mod, m => m.GameModFolders)) continue;
                var enabledPath = Path.Combine(gameModsPath, folder);
                var disabledPath = Path.Combine(gameModsPath, "~" + folder);

                if (Directory.Exists(enabledPath))
                {
                    if (Directory.Exists(disabledPath))
                        Directory.Delete(disabledPath, true);
                    Directory.Move(enabledPath, disabledPath);
                }
            }

            // Update InstalledPath to disabled path (primary folder)
            if (mod.GameModFolders.Count > 0)
                mod.InstalledPath = Path.Combine(gameModsPath, "~" + mod.GameModFolders[0]);

            // Disable each DLC folder
            if (mod.GameDlcFolders.Count > 0)
            {
                var gameDlcPath = GetGameDlcPath();
                foreach (var dlcFolder in mod.GameDlcFolders)
                {
                    if (IsSharedFolder(dlcFolder, mod, m => m.GameDlcFolders)) continue;
                    ToggleFolder(gameDlcPath, dlcFolder, false);
                }
            }

            // Update mods.settings
            if (mod.ModType == ModType.Mod)
            {
                var currentMods = GetEnabledModsFromSettings();
                foreach (var folder in mod.GameModFolders)
                    currentMods.RemoveAll(m => m.ModName == folder);
                WriteModsSettings(currentMods);
            }

            return true;
        }
        catch { return false; }
    }
    public bool DeleteMod(ModEntry mod)
    {
        try
        {
            var failed = false;
            var gameModsPath = GetGameModsPath();
            var storageDir = _storage?.GetModsDirectory() ?? Path.GetDirectoryName(mod.InstalledPath) ?? "";

            // Recovery for records created before folder-list plumbing: when neither
            // deployment list exists, derive it from the canonical GameModFolderName so
            // Delete can clean legacy entries (structural rule, not a per-mod case).
            if (mod.GameModFolders.Count == 0 && mod.GameDlcFolders.Count == 0
                && !string.IsNullOrEmpty(mod.GameModFolderName))
            {
                if (mod.ModType == ModType.Dlc)
                    mod.GameDlcFolders.Add(mod.GameModFolderName);
                else
                    mod.GameModFolders.Add(mod.GameModFolderName);
            }

            // Delete all mod folders from game mods dir
            foreach (var folder in mod.GameModFolders)
            {
                if (IsSharedFolder(folder, mod, m => m.GameModFolders)) continue;
                var enabledPath = Path.Combine(gameModsPath, folder);
                var disabledPath = Path.Combine(gameModsPath, "~" + folder);
                DeleteIfExists(enabledPath, ref failed);
                DeleteIfExists(disabledPath, ref failed);
            }

            // Delete all DLC folders from game dlc dir
            if (mod.GameDlcFolders.Count > 0)
            {
                var gameDlcPath = GetGameDlcPath();
                foreach (var dlcFolder in mod.GameDlcFolders)
                {
                    if (IsSharedFolder(dlcFolder, mod, m => m.GameDlcFolders)) continue;
                    var dlcEnabled = Path.Combine(gameDlcPath, dlcFolder);
                    var dlcDisabled = Path.Combine(gameDlcPath, "~" + dlcFolder);
                    DeleteIfExists(dlcEnabled, ref failed);
                    DeleteIfExists(dlcDisabled, ref failed);
                }
            }

            // Delete storage folders
            foreach (var folder in mod.GameModFolders)
            {
                if (IsSharedFolder(folder, mod, m => m.GameModFolders)) continue;
                DeleteIfExists(Path.Combine(storageDir, folder), ref failed);
            }
            foreach (var dlcFolder in mod.GameDlcFolders)
            {
                if (IsSharedFolder(dlcFolder, mod, m => m.GameDlcFolders)) continue;
                DeleteIfExists(Path.Combine(storageDir, dlcFolder), ref failed);
            }

            // Delete primary installed path
            if (!string.IsNullOrEmpty(mod.InstalledPath))
            {
                var primaryName = Path.GetFileName(mod.InstalledPath.TrimEnd('\\', '/'));
                if (primaryName.StartsWith("~", StringComparison.Ordinal))
                    primaryName = primaryName[1..];
                var sharedPrimary = IsSharedFolder(primaryName, mod, m => m.GameModFolders)
                                 || IsSharedFolder(primaryName, mod, m => m.GameDlcFolders);
                if (!sharedPrimary)
                    DeleteIfExists(mod.InstalledPath, ref failed);
            }

            // Update mods.settings
            try
            {
                var currentMods = GetEnabledModsFromSettings();
                foreach (var folder in mod.GameModFolders)
                {
                    if (IsSharedFolder(folder, mod, m => m.GameModFolders)) continue;
                    currentMods.RemoveAll(m => m.ModName == folder);
                }
                WriteModsSettings(currentMods);
            }
            catch { }

            // Delete bin files
            DeleteBinFiles(mod);

            // A record with nothing left on disk is still successfully removed; only a
            // real deletion failure (path exists but could not be deleted) means stale.
            return !failed;
        }
        catch { return false; }
    }

    private static void DeleteIfExists(string path, ref bool failed)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch { failed = true; }
    }



    public List<(string ModName, int Priority)> GetEnabledModsFromSettings()
    {
        var result = new List<(string, int)>();
        var path = GetModsSettingsPath();
        if (path == null || !File.Exists(path)) return result;

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var match = Regex.Match(line, @"^\s*""([^""]+)""\s*=\s*(\d+)\s*$");
                if (match.Success)
                    result.Add((match.Groups[1].Value, int.Parse(match.Groups[2].Value)));
            }
        }
        catch { }
        return result;
    }

    private HashSet<string> GetManagedFolderKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_storage == null) return keys;
        foreach (var m in _storage.GetAllMods())
        {
            foreach (var f in m.GameModFolders)
                if (!string.IsNullOrEmpty(f)) keys.Add(f);
            foreach (var f in m.GameDlcFolders)
                if (!string.IsNullOrEmpty(f)) keys.Add(f);
            if (!string.IsNullOrEmpty(m.GameModFolderName))
                keys.Add(m.GameModFolderName);
        }
        return keys;
    }
    public void WriteModsSettings(List<(string ModName, int Priority)> mods)
    {
        try
        {
            var path = ComputeModsSettingsPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Only keys this manager owns (registered by a stored ModEntry) may be added,
            // updated or removed. Foreign parseable rows, comments, blanks and unknown
            // sections are preserved byte-for-byte and never re-sorted or re-written.
            var owned = _storage != null
                ? GetManagedFolderKeys()
                : new HashSet<string>(mods.Select(m => m.ModName), StringComparer.OrdinalIgnoreCase);
            var desired = mods.Where(m => owned.Contains(m.ModName)).ToList();

            var preserved = new List<string>();
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var match = Regex.Match(line, @"^\s*""([^""]+)""\s*=\s*(\d+)\s*$");
                    // Drop an existing line only if it is one of OUR owned keys; the desired
                    // list decides which of those survive (stale/disabled owned rows vanish).
                    if (match.Success && owned.Contains(match.Groups[1].Value))
                        continue;
                    preserved.Add(line);
                }
            }

            var sorted = desired.OrderBy(m => m.Priority).ToList();
            var managedLines = sorted.Select(m => $@"""{m.ModName}"" = {m.Priority}");
            var allLines = preserved.Concat(managedLines).ToList();

            // Atomic write: temp file on same volume, then replace.
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, allLines);
            if (File.Exists(path))
                File.Replace(tmp, path!, null, true);
            else
                File.Move(tmp, path!);
        }
        catch { }
    }

    public void UpdateAllPriorities(List<(string? GameModFolderName, int Priority)> mods)
    {
        WriteModsSettings(mods.Select(m => (m.GameModFolderName ?? "", m.Priority)).ToList());
    }

    public void LaunchGame()
    {
        if (_isLaunching) return;
        _isLaunching = true;
        try
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "steam://rungameid/292030",
                    UseShellExecute = true
                });
            }
            catch
            {
                var gamePath = GetGamePath();
                if (gamePath != null)
                {
                    var exePath = Path.Combine(gamePath, "bin", "x64", "witcher3.exe");
                    if (File.Exists(exePath))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = Path.GetDirectoryName(exePath) ?? gamePath,
                            UseShellExecute = true
                        });
                }
            }
        }
        finally
        {
            _ = Task.Run(async () => { await Task.Delay(5000); _isLaunching = false; });
        }
    }

    private static string EnsureModPrefix(string name, ModType modType)
    {
        var prefix = modType == ModType.Dlc ? "dlc" : "mod";
        var clean = name.StartsWith("~") ? name[1..] : name;
        var lower = clean.ToLower();
        if (lower.StartsWith("mod") || lower.StartsWith("dlc"))
            return clean;
        return prefix + clean;
    }

    /// <summary>
    /// Enable or disable a single folder by renaming (add/remove ~ prefix).
    /// </summary>
    private static bool ToggleFolder(string gameDir, string folderName, bool enable)
    {
        var cleanName = folderName.StartsWith("~") ? folderName.Substring(1) : folderName;
        var enabledPath = System.IO.Path.Combine(gameDir, cleanName);
        var disabledPath = System.IO.Path.Combine(gameDir, "~" + cleanName);

        if (enable && Directory.Exists(disabledPath))
        {
            if (Directory.Exists(enabledPath)) Directory.Delete(enabledPath, true);
            Directory.Move(disabledPath, enabledPath);
            return true;
        }
        if (!enable && Directory.Exists(enabledPath))
        {
            if (Directory.Exists(disabledPath)) Directory.Delete(disabledPath, true);
            Directory.Move(enabledPath, disabledPath);
            return true;
        }
        return false;
    }

    public static string SanitizeModName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c) && c != ' ').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "custom_mod" : sanitized;
    }

    private static void MoveDirectory(string sourceDir, string targetDir)
    {
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);
        try { Directory.Move(sourceDir, targetDir); return; }
        catch (IOException)
        {
            CopyDirectoryRecursive(sourceDir, targetDir);
            Directory.Delete(sourceDir, true);
        }
    }

    private void CopyDlcToGame(ModEntry mod, string dlcPath)
    {
        if (string.IsNullOrEmpty(dlcPath) || string.IsNullOrEmpty(mod.GameDlcFolderName)) return;
        var storageDlc = Path.Combine(Path.GetDirectoryName(mod.InstalledPath) ?? "", mod.GameDlcFolderName);
        if (Directory.Exists(storageDlc))
        {
            var target = Path.Combine(dlcPath, mod.GameDlcFolderName);
            if (!Directory.Exists(target))
                CopyDirectoryRecursive(storageDlc, target);
        }
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

    private void EnableBinFiles(ModEntry mod)
    {
        if (mod.GameBinFiles.Count == 0 || string.IsNullOrEmpty(mod.BinStoragePath)) return;
        var gamePath = GetGamePath();
        if (gamePath == null) return;
        var gameBinRoot = Path.Combine(gamePath, "bin");
        foreach (var bf in mod.GameBinFiles)
        {
            var src = Path.Combine(mod.BinStoragePath, bf.Replace('/', Path.DirectorySeparatorChar));
            var dest = Path.Combine(gameBinRoot, bf.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, true);
        }
    }

    /// <summary>
    /// Bin files are NEVER automatically deleted from the game directory.
    /// Multiple mods may share the same bin file (e.g. config XMLs).
    /// Removing one mod's bin could break another mod silently.
    /// </summary>
    private void DisableBinFiles(ModEntry mod)
    {
        // Intentionally empty — bin files persist in game directory
    }

    private void DeleteBinFiles(ModEntry mod)
    {
        // Clean storage copy only — game bin files are intentionally left in place
        if (!string.IsNullOrEmpty(mod.BinStoragePath) && Directory.Exists(mod.BinStoragePath))
        {
            try { Directory.Delete(mod.BinStoragePath, true); } catch { }
        }

        // Notify user that game bin files were NOT removed
        if (mod.GameBinFiles.Count > 0)
        {
            var fileList = string.Join("\n\uff65 ", mod.GameBinFiles.Take(5));
            if (mod.GameBinFiles.Count > 5) fileList += "\n\uff65 ...";
            System.Windows.MessageBox.Show(
                string.Format(TranslationService.Instance.T("bin.delete_warning"), mod.GameBinFiles.Count,fileList),
                TranslationService.Instance.T("bin.notice"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    private bool IsSharedFolder(string folder, ModEntry current, Func<ModEntry, List<string>> selector)
    {
        if (_storage == null) return false;
        foreach (var other in _storage.GetAllMods())
        {
            if (other.Id == current.Id || !other.IsEnabled) continue;
            if (selector(other).Contains(folder, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
