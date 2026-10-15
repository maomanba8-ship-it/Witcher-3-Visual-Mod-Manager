using System.IO;
using System.Text.Json;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class ModStorageService
{
    private readonly string _dataDir;
    private readonly string _modsDir;
    private readonly string _binDir;
    private readonly string _dbPath;
    private readonly Core.Deployment.LibraryIndexStore _indexStore;
    private readonly Core.Deployment.OperationJournalStore _journals;
    private List<ModEntry> _mods = new();
    private Core.Deployment.LibraryIndex _index = Core.Deployment.LibraryIndex.Empty;

    /// <summary>
    /// How the library file was read. The UI reports a damaged or unreadable library instead of
    /// letting the user discover an empty list with no explanation.
    /// </summary>
    public Core.Storage.TextFileOutcome LibraryLoadOutcome { get; private set; } = Core.Storage.TextFileOutcome.Missing;

    /// <summary>Where a damaged library file was copied before anything could overwrite it.</summary>
    public string? DamagedLibraryCopyPath { get; private set; }

    public ModStorageService()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataPath.FolderName);
        _modsDir = Path.Combine(_dataDir, "mods");
        _binDir = Path.Combine(_dataDir, "bin");
        _dbPath = Path.Combine(_dataDir, "mods.json");
        _indexStore = new Core.Deployment.LibraryIndexStore(Path.Combine(_dataDir, "library.json"));
        _journals = new Core.Deployment.OperationJournalStore(Path.Combine(_dataDir, "journal"));
        
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_modsDir);
        Directory.CreateDirectory(_binDir);
        Load();
        _index = _indexStore.Load();
    }

    /// <summary>
    /// The ownership index: which package put which game target where. Derived data, so it is
    /// loaded independently of the library and can be rebuilt by observing reality again.
    /// </summary>
    public Core.Deployment.LibraryIndex Index => _index;

    public string? IndexSaveError => _indexStore.LastSaveError;

    /// <summary>
    /// The journal of running operations. A file still present at startup is an operation that did
    /// not finish, which is reported to the user rather than acted on.
    /// </summary>
    public Core.Deployment.OperationJournalStore Journals => _journals;

    /// <summary>
    /// Applies a change to the index and persists it. A failed save is reported but never
    /// propagated: the index is derived data, and losing a write only degrades the answer to
    /// "who owns this?" back to "unknown", which is the conservative direction. It must not be
    /// able to fail the operation that triggered it.
    /// </summary>
    public void UpdateIndex(Func<Core.Deployment.LibraryIndex, Core.Deployment.LibraryIndex> change)
    {
        if (change is null) return;

        _index = change(_index) ?? _index;
        if (_indexStore.Save(_index)) return;

#if DEBUG
        DiagnosticLog.Write($"[index] failed to save the library index: {_indexStore.LastSaveError}");
#endif
    }

    /// <summary>
    /// Components a record declares, with no targets yet: at import time nothing has been
    /// deployed, and an empty target list is exactly what makes "enabled but nothing on disk"
    /// detectable later.
    /// </summary>
    private static IReadOnlyList<ComponentRecord> ComponentsOf(ModEntry entry)
    {
        var components = new List<ComponentRecord>();

        foreach (var folder in entry.GameModFolders ?? new List<string>())
            components.Add(Component(entry.Id, TargetDomain.Mods, folder));

        foreach (var folder in entry.GameDlcFolders ?? new List<string>())
            components.Add(Component(entry.Id, TargetDomain.Dlc, folder));

        if ((entry.GameBinFiles?.Count ?? 0) > 0)
            components.Add(Component(entry.Id, TargetDomain.Bin, entry.BinStoragePath ?? ""));

        return components;
    }

    private static ComponentRecord Component(string packageId, TargetDomain domain, string folder) =>
        new(ComponentRecord.MakeId(packageId, domain, folder), domain, folder,
            Array.Empty<TargetRecord>());

    /// <summary>
    /// Remembers a package's declared components in the index. Any target a component already
    /// owns is kept: importing replaces the storage copy, it does not undeploy.
    /// </summary>
    public void IndexPackage(ModEntry entry)
    {
        if (entry is null) return;
        UpdateIndex(index =>
            LibraryIndexUpdater.WithDeclaredComponents(index, entry.Id, entry.Name, ComponentsOf(entry)));
    }

    /// <summary>Forgets a package and its target claims.</summary>
    public void IndexForget(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return;
        UpdateIndex(index => LibraryIndexUpdater.WithoutPackage(index, packageId));
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
    /// Read-only legacy data check. Returns warnings about data that would normally be
    /// migrated/deduplicated, but does NOT modify the in-memory list and never calls Save().
    /// Caller should surface these as a non-blocking notice or log.
    /// </summary>
    public List<string> GetLegacyDataWarnings()
    {
        var warnings = new List<string>();

        var dupGroups = _mods
            .Where(m => !string.IsNullOrEmpty(m.GameModFolderName))
            .GroupBy(m => m.GameModFolderName!)
            .Where(g => g.Count() > 1)
            .ToList();

        if (dupGroups.Count > 0)
            warnings.Add($"发现 {dupGroups.Count} 组重复的模组目录，建议手动去重（未自动处理）。");

        var grouped = _mods
            .Where(m => !string.IsNullOrEmpty(m.GroupId))
            .GroupBy(m => m.GroupId!)
            .Where(g => g.Count() > 1)
            .ToList();

        if (grouped.Count > 0)
            warnings.Add($"发现 {grouped.Count} 组旧版分组数据，建议手动迁移（未自动处理）。");

        return warnings;
    }

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
                if (!string.IsNullOrEmpty(dlcEntry.GameModFolderName) && !modEntry.GameDlcFolders.Contains(dlcEntry.GameModFolderName)) modEntry.GameDlcFolders.Add(dlcEntry.GameModFolderName);
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
                    if (!string.IsNullOrEmpty(dupe.GameDlcFolderName) && !keeper.GameDlcFolders.Contains(dupe.GameDlcFolderName)) keeper.GameDlcFolders.Add(dupe.GameDlcFolderName);
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
        IndexPackage(mod);
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
        IndexPackage(mod);
    }

    public void RemoveMod(string id)
    {
        var mod = _mods.FirstOrDefault(m => m.Id == id);
        if (mod != null)
        {
            // InstalledPath cleanup handled by Witcher3Service.DeleteMod
            _mods.Remove(mod);
            Save();
            IndexForget(id);
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

    public string GetBinDirectory() => _binDir;

    public string GetModBinDirectory(string modId)
    {
        var dir = Path.Combine(_binDir, modId);
        Directory.CreateDirectory(dir);
        return dir;
    }


        public ModEntry ImportOrphanMod(OrphanModInfo orphan)
    {
        var cleanName = GetBaseName(orphan);
        var storageDir = Path.Combine(_modsDir, cleanName);

        // Copy from game dir to storage (mod component)
        if (!Directory.Exists(storageDir))
            CopyDirectoryRecursive(orphan.FullPath, storageDir);

        var entry = new ModEntry
        {
            Name = cleanName,
            InstalledPath = orphan.FullPath,
            IsEnabled = orphan.IsEnabled,
            ImportDate = DateTime.Now,
            Priority = _mods.Count > 0 ? _mods.Max(m => m.Priority) + 1 : 1
        };

        if (orphan.IsInDlcFolder)
            entry.GameDlcFolders.Add(orphan.FolderName.StartsWith("~") ? orphan.FolderName[1..] : orphan.FolderName);
        else
            entry.GameModFolders.Add(orphan.FolderName.StartsWith("~") ? orphan.FolderName[1..] : orphan.FolderName);

        // Handle paired orphan (Mod + DLC)
        if (orphan.PairedOrphan != null)
        {
            var pair = orphan.PairedOrphan;
            var pairCleanName = pair.FolderName.StartsWith("~") ? pair.FolderName[1..] : pair.FolderName;

            if (pair.IsInDlcFolder)
                entry.GameDlcFolders.Add(pairCleanName);
            else
                entry.GameModFolders.Add(pairCleanName);
        }

        // Deduplicate check (check all folders)
        if (!string.IsNullOrEmpty(cleanName))
        {
            var exists = _mods.Any(m =>
                m.GameModFolders.Contains(cleanName) || m.GameDlcFolders.Contains(cleanName));
            if (exists) return entry;
        }

        _mods.Add(entry);
        Save();
        return entry;
    }

    private static string GetBaseName(OrphanModInfo orphan)
    {
        var name = orphan.FolderName.StartsWith("~") ? orphan.FolderName[1..] : orphan.FolderName;
        if (name.StartsWith("mod", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            return name[3..];
        if (name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            return name[3..];
        return name;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
    public string GetModsDirectory() => _modsDir;

    /// <summary>
    /// Read-only audit of the library as it stands: folders that are not on disk, shared folders,
    /// enabled mods the game directory does not have, and records that name no component at all.
    /// Nothing is modified, so this is safe to run at startup and to base a migration decision on.
    /// </summary>
    public Core.Migration.LegacyLibraryAudit GetLibraryAudit(string? gamePath)
    {
        var entries = _mods.Select(m => new Core.Migration.LegacyLibraryEntry(
            m.Id,
            string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name,
            m.IsEnabled,
            m.GameModFolders ?? new List<string>(),
            m.GameDlcFolders ?? new List<string>(),
            m.GameBinFiles ?? new List<string>()));

        var hasGame = !string.IsNullOrWhiteSpace(gamePath);
        var gameMods = hasGame ? Path.Combine(gamePath!, "mods") : null;
        var gameDlc = hasGame ? Path.Combine(gamePath!, "dlc") : null;

        return Core.Migration.LegacyLibraryAuditor.Audit(
            entries,
            folder => Directory.Exists(Path.Combine(_modsDir, folder)),
            hasGame
                ? (folder, isDlc) => Directory.Exists(Path.Combine(isDlc ? gameDlc! : gameMods!, folder))
                : null);
    }

    private void Load()
    {
        // A damaged library used to fall through to an empty list, and the next save then wrote
        // that empty list over the user's mods. Now the damaged file is copied aside first and
        // the previous good version (.bak) is used when it is available.
        var load = Core.Storage.TextFileRecovery.Load(_dbPath, LooksLikeALibrary);
        LibraryLoadOutcome = load.Outcome;
        DamagedLibraryCopyPath = load.Detail;

        _mods = load.Text is not null && TryDeserialize(load.Text, out var mods)
            ? mods
            : new List<ModEntry>();
    }

    private void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_mods, options);
        Core.Storage.AtomicFile.WriteAllText(_dbPath, json);
    }

    private static bool LooksLikeALibrary(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ModEntry>>(json) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeserialize(string json, out List<ModEntry> mods)
    {
        try
        {
            mods = JsonSerializer.Deserialize<List<ModEntry>>(json) ?? new List<ModEntry>();
            return true;
        }
        catch
        {
            mods = new List<ModEntry>();
            return false;
        }
    }
}
