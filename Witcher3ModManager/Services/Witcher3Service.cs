using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Witcher3ModManager.Core.Manifest;
using Witcher3ModManager.Core.Shared;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class Witcher3Service
{
    private readonly SettingsService _settings; private readonly ModStorageService? _storage;

    /// <summary>
    /// Where an operation parks whatever it has to move out of the way. Kept inside the game
    /// directory so those moves stay on the same volume and cost nothing but a rename.
    /// </summary>
    private const string StagingFolderName = "_w3mm_staging";
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
            // The user may have picked the exe, the bin folder or a bin subfolder; resolve upward
            // to the installation root. Falls back to what they gave us so an install whose exe is
            // missing but still has a content folder is accepted as before.
            normalized = GameLayout.FindGameRoot(normalized) ?? normalized;
            if (Directory.Exists(Path.Combine(normalized, "content")) ||
                GameLayout.IsGameRoot(normalized))
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
            if (Directory.Exists(p) && GameLayout.IsGameRoot(p))
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
        var journal = BeginJournal(OperationKind.Enable, mod);
        try
        {
            var gameModsPath = GetGameModsPath();
            var storageDir = _storage?.GetModsDirectory() ?? System.IO.Path.GetDirectoryName(mod.InstalledPath) ?? "";

            // Everything is decided up front and then carried out by the deployer. It never
            // deletes a target outright: whatever occupies the name is moved aside first, so a
            // failure part-way through can put the previous state back.
            var plan = EnablePlanner.Plan(
                gameModsPath,
                GetGameDlcPath(),
                storageDir,
                mod.GameModFolders,
                mod.GameDlcFolders);

            var gameRoot = Path.GetDirectoryName(gameModsPath) ?? gameModsPath;
            var result = new DeploymentEngine(Path.Combine(gameRoot, StagingFolderName))
                .Execute(plan.ToDeploymentPlan(), journal?.OperationId);

            foreach (var step in result.Steps.Where(s => s.Outcome == DeploymentStepOutcome.Skipped))
                DiagnosticLog.Write($"[deploy] nothing to do for {step.Target} ({step.Action})");

            if (!result.Ok)
            {
                _storage?.Journals.Fail(journal, result.Failure);
                return false;
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

            // Update InstalledPath only after the whole enable succeeded.
            if (mod.GameModFolders.Count > 0)
                mod.InstalledPath = System.IO.Path.Combine(gameModsPath, mod.GameModFolders[0]);

            RecordDeployedTargets(mod);
            _storage?.Journals.Complete(journal?.OperationId);
            return true;
        }
        catch (Exception ex)
        {
            _storage?.Journals.Fail(journal, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Starts a journal entry for an operation, listing the paths it intends to touch. Returns null
    /// when no journal could be written: that is worth logging, but it must not stop the operation
    /// the user asked for.
    /// </summary>
    private OperationJournal? BeginJournal(OperationKind kind, ModEntry mod)
    {
        try
        {
            var targets = mod.GameModFolders.Select(f => "mods/" + f)
                .Concat(mod.GameDlcFolders.Select(f => "dlc/" + f))
                .Concat(mod.GameBinFiles.Select(f => "bin/" + f))
                .ToList();

            return _storage?.Journals.Begin(kind, mod.Name, targets);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"[journal] could not start a {kind} journal for {mod.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Records what this enable actually put in the game directory, together with the content that
    /// was written there, so a later operation can tell "ours" from "someone else's".
    ///
    /// Called only after the deployment succeeded, and deliberately unable to fail it: the index
    /// is derived data, so losing a write degrades the answer to "who owns this?" back to
    /// unknown, which is the conservative direction. Reporting a successful enable as failed
    /// because of an index write would be far worse than an incomplete index.
    ///
    /// Disabling does not touch the index: the recorded target stays, and the state evaluator
    /// already reports a disabled package whose folder is not there as simply not deployed.
    /// </summary>
    private void RecordDeployedTargets(ModEntry mod)
    {
        if (_storage is null) return;
        var gamePath = GetGamePath();
        if (string.IsNullOrWhiteSpace(gamePath)) return;

        try
        {
            var components = new List<ComponentRecord>();

            foreach (var folder in mod.GameModFolders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                components.Add(new ComponentRecord(
                    ComponentRecord.MakeId(mod.Id, TargetDomain.Mods, folder),
                    TargetDomain.Mods,
                    folder,
                    new[] { FolderTarget("mods/" + folder, Path.Combine(gamePath, "mods", folder), mod.Id, TargetDomain.Mods) }));
            }

            foreach (var folder in mod.GameDlcFolders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                components.Add(new ComponentRecord(
                    ComponentRecord.MakeId(mod.Id, TargetDomain.Dlc, folder),
                    TargetDomain.Dlc,
                    folder,
                    new[] { FolderTarget("dlc/" + folder, Path.Combine(gamePath, "dlc", folder), mod.Id, TargetDomain.Dlc) }));
            }

            if (mod.GameBinFiles.Count > 0)
            {
                var binTargets = mod.GameBinFiles
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => FileTarget("bin/" + f,
                        Path.Combine(gamePath, "bin", f.Replace('/', Path.DirectorySeparatorChar))))
                    .ToList();

                components.Add(new ComponentRecord(
                    ComponentRecord.MakeId(mod.Id, TargetDomain.Bin, ""),
                    TargetDomain.Bin,
                    mod.BinStoragePath ?? "",
                    binTargets));
            }

            _storage.UpdateIndex(index =>
                LibraryIndexUpdater.WithPackage(index, mod.Id, mod.Name, components));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"[index] could not record deployed targets for {mod.Name}: {ex.Message}");
        }
    }

    private static TargetRecord FolderTarget(string relativePath, string fullPath, string packageId, TargetDomain domain) =>
        new(relativePath, TargetKind.Folder, FolderFingerprint(fullPath, packageId, domain), DateTime.Now);

    private static TargetRecord FileTarget(string relativePath, string fullPath) =>
        new(relativePath, TargetKind.File, FileSha256(fullPath), DateTime.Now);

    /// <summary>
    /// Fingerprint of a deployed folder. Computed from the game copy rather than reused from the
    /// import-time record, because a shared storage folder can legitimately hold another package's
    /// content by now, and an ownership hash that describes the wrong bytes is worse than none.
    /// Returns "" when it cannot be read, which callers treat as "unknown", not as a match.
    /// </summary>
    private static string FolderFingerprint(string root, string packageId, TargetDomain domain)
    {
        try
        {
            return Directory.Exists(root)
                ? ComponentFingerprint.Compute(ManifestBuilder.Build(root, packageId, domain))
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string FileSha256(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    public bool DisableMod(ModEntry mod, string? storageModsDir = null)
    {
        var journal = BeginJournal(OperationKind.Disable, mod);
        try
        {
            var gameModsPath = GetGameModsPath();

            // A shared folder is left untouched: another enabled mod still points at it, so
            // renaming it would disable that mod as well.
            var modFolders = mod.GameModFolders
                .Where(f => !IsSharedFolder(f, mod, TargetDomain.Mods))
                .ToList();
            var dlcFolders = mod.GameDlcFolders
                .Where(f => !IsSharedFolder(f, mod, TargetDomain.Dlc))
                .ToList();

            var plan = DisablePlanner.Plan(gameModsPath, GetGameDlcPath(), modFolders, dlcFolders);
            var gameRoot = Path.GetDirectoryName(gameModsPath) ?? gameModsPath;
            var result = new DeploymentEngine(Path.Combine(gameRoot, StagingFolderName))
                .Execute(plan, journal?.OperationId);

            foreach (var step in result.Steps.Where(s => s.Outcome == DeploymentStepOutcome.Skipped))
                DiagnosticLog.Write($"[deploy] nothing to disable for {step.Target}");

            if (!result.Ok)
            {
                _storage?.Journals.Fail(journal, result.Failure);
                return false;
            }

            // Update mods.settings
            if (mod.ModType == ModType.Mod)
            {
                var currentMods = GetEnabledModsFromSettings();
                foreach (var folder in mod.GameModFolders)
                    currentMods.RemoveAll(m => m.ModName == folder);
                WriteModsSettings(currentMods);
            }

            // Update InstalledPath only after the whole disable succeeded.
            if (mod.GameModFolders.Count > 0)
                mod.InstalledPath = Path.Combine(gameModsPath, "~" + mod.GameModFolders[0]);

            _storage?.Journals.Complete(journal?.OperationId);
            return true;
        }
        catch (Exception ex)
        {
            _storage?.Journals.Fail(journal, ex.Message);
            return false;
        }
    }
    public bool DeleteMod(ModEntry mod)
    {
        var journal = BeginJournal(OperationKind.Delete, mod);
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
                if (IsSharedFolder(folder, mod, TargetDomain.Mods)) continue;
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
                    if (IsSharedFolder(dlcFolder, mod, TargetDomain.Dlc)) continue;
                    var dlcEnabled = Path.Combine(gameDlcPath, dlcFolder);
                    var dlcDisabled = Path.Combine(gameDlcPath, "~" + dlcFolder);
                    DeleteIfExists(dlcEnabled, ref failed);
                    DeleteIfExists(dlcDisabled, ref failed);
                }
            }

            // Delete storage folders
            foreach (var folder in mod.GameModFolders)
            {
                if (IsSharedFolder(folder, mod, TargetDomain.Mods)) continue;
                DeleteIfExists(Path.Combine(storageDir, folder), ref failed);
            }
            foreach (var dlcFolder in mod.GameDlcFolders)
            {
                if (IsSharedFolder(dlcFolder, mod, TargetDomain.Dlc)) continue;
                DeleteIfExists(Path.Combine(storageDir, dlcFolder), ref failed);
            }

            // Delete primary installed path
            if (!string.IsNullOrEmpty(mod.InstalledPath))
            {
                var primaryName = Path.GetFileName(mod.InstalledPath.TrimEnd('\\', '/'));
                if (primaryName.StartsWith("~", StringComparison.Ordinal))
                    primaryName = primaryName[1..];
                var sharedPrimary = IsSharedFolder(primaryName, mod, TargetDomain.Mods)
                                 || IsSharedFolder(primaryName, mod, TargetDomain.Dlc);
                if (!sharedPrimary)
                    DeleteIfExists(mod.InstalledPath, ref failed);
            }

            // If a game/storage folder could not be deleted, do NOT touch mods.settings or
            // the storage bin copy; keep the record intact so the user can retry.
            if (!failed)
            {
                // Update mods.settings
                try
                {
                    var currentMods = GetEnabledModsFromSettings();
                    foreach (var folder in mod.GameModFolders)
                    {
                        if (IsSharedFolder(folder, mod, TargetDomain.Mods)) continue;
                        currentMods.RemoveAll(m => m.ModName == folder);
                    }
                    WriteModsSettings(currentMods);
                }
                catch { }

                // Delete bin files
                DeleteBinFiles(mod);
            }

            // A record with nothing left on disk is still successfully removed; only a
            // real deletion failure (path exists but could not be deleted) means stale.
            if (!failed) _storage?.Journals.Complete(journal?.OperationId);
            else _storage?.Journals.Fail(journal, "one or more paths could not be deleted");

            return !failed;
        }
        catch (Exception ex)
        {
            _storage?.Journals.Fail(journal, ex.Message);
            return false;
        }
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
            // Without a resolved Documents folder this would be a relative path and the file
            // would land next to the executable instead of where the game reads it.
            if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
                throw new InvalidOperationException("The Documents folder could not be resolved, so mods.settings has no safe location.");

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

            // Same-volume temporary file, atomic replace, and the previous file kept as .bak:
            // this is the file the game reads for load order, so a half-written file or a lost
            // previous version is not acceptable.
            Core.Storage.AtomicFile.WriteAllLines(path, allLines);
        }
        catch (Exception ex)
        {
#if DEBUG
            DiagnosticLog.Write($"[settings] failed to write mods.settings: {ex.Message}");
#endif
            // Reporting matters here: failing silently would leave the game with a different
            // load order than the one the list is showing.
            System.Windows.MessageBox.Show(
                string.Format(TranslationService.Instance.T("settings.save_failed"), ex.Message),
                TranslationService.Instance.T("dialog.error"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
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
                var exePath = GameLayout.ResolveGameExecutable(gamePath);
                if (exePath != null)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = Path.GetDirectoryName(exePath) ?? gamePath!,
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

    private bool IsSharedFolder(string folder, ModEntry current, TargetDomain domain)
    {
        if (_storage == null) return false;

        var claims = new List<FolderClaim>();
        foreach (var m in _storage.GetAllMods())
        {
            if (!m.IsEnabled) continue;
            var folders = domain == TargetDomain.Dlc ? m.GameDlcFolders : m.GameModFolders;
            foreach (var f in folders)
                if (!string.IsNullOrWhiteSpace(f))
                    claims.Add(new FolderClaim(m.Id, domain, f));
        }

        return SharedResourceResolver.IsSharedByOther(claims, domain, folder, current.Id);
    }
}
