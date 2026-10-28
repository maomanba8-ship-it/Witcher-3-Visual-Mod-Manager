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
    private readonly Core.Deployment.BinLedgerStore _binLedgerStore;
    private readonly Core.Deployment.BinBackupStore _binBackups;
    private List<ModEntry> _mods = new();
    private Core.Deployment.LibraryIndex _index = Core.Deployment.LibraryIndex.Empty;
    private Core.Deployment.BinLedger _binClaims = Core.Deployment.BinLedger.Empty;

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
        _binLedgerStore = new Core.Deployment.BinLedgerStore(Path.Combine(_dataDir, "bin-ledger.json"));
        _binBackups = new Core.Deployment.BinBackupStore(Path.Combine(_dataDir, "binbackup"));
        
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_modsDir);
        Directory.CreateDirectory(_binDir);
        Load();
        _index = _indexStore.Load();
        PruneStaleIndexEntries();
        _binClaims = _binLedgerStore.Load();
    }

    /// <summary>
    /// The index is derived data. A record can be replaced or removed outside this process (or by
    /// an older build that did not clean up), leaving a package that still claims folders nobody
    /// owns. Dropping those entries at startup keeps adoption and drift detection honest; the
    /// folder itself is never touched.
    /// </summary>
    private void PruneStaleIndexEntries()
    {
        var pruned = Core.Deployment.LibraryIndexUpdater.WithoutUnknownPackages(
            _index, _mods.Select(m => m.Id));
        if (ReferenceEquals(pruned, _index)) return;

        _index = pruned;
        _indexStore.Save(_index);
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
    /// What the manager wrote into the game's bin directory, and what was there first. Losing this
    /// only costs the ability to put bin files back, which is why a failed save is reported and
    /// then ignored rather than allowed to fail the operation.
    /// </summary>
    public Core.Deployment.BinLedger BinClaims => _binClaims;

    public string? BinLedgerSaveError => _binLedgerStore.LastSaveError;

    public Core.Storage.TextFileOutcome BinLedgerLoadOutcome => _binLedgerStore.LastLoadOutcome;

    /// <summary>Where copies of overwritten bin files are kept, addressed by content hash.</summary>
    public Core.Deployment.BinBackupStore BinBackups => _binBackups;

    /// <summary>
    /// Applies a change to the bin claims and persists them. Like the index, this is derived data
    /// in one direction only: without it nothing can be put back, but nothing on disk is wrong,
    /// so it must never be able to fail the operation that triggered it.
    /// </summary>
    public void UpdateBinClaims(Func<Core.Deployment.BinLedger, Core.Deployment.BinLedger> change)
    {
        if (change is null) return;

        var updated = change(_binClaims) ?? _binClaims;
        if (_binLedgerStore.Save(updated))
        {
            _binClaims = updated;
            return;
        }

#if DEBUG
        DiagnosticLog.Write($"[bin] failed to save the bin ledger: {_binLedgerStore.LastSaveError}");
#endif
        // Kept in memory even when the save failed: this session can still undo its own writes.
        _binClaims = updated;
    }

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
            {
                _mods.Remove(existing);
                // Its index entry describes a record that is about to stop existing. Leaving it
                // behind keeps claiming folders the replacement may not own.
                IndexForget(existing.Id);
            }
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


    /// <summary>The outcome of taking one orphan into the library.</summary>
    public sealed record AdoptionOutcome(
        ModEntry? Entry,
        IReadOnlyList<Core.Deployment.AdoptionWarning> Warnings)
    {
        public bool Adopted => Entry is not null;
    }

    /// <summary>
    /// Takes a folder that is already in the game directory into the library.
    ///
    /// Nothing is copied and nothing is deleted: the record is created and the index remembers what
    /// is there, so later operations can tell this package's content from someone else's. Copying
    /// the folder into storage would double the disk usage of every mod the user installed by hand,
    /// which is why the old import was the wrong shape for this. The storage copy stays absent, in
    /// the same state the audit already describes as "not an anomaly" for earlier mods.
    /// </summary>
    public AdoptionOutcome AdoptOrphan(OrphanModInfo orphan)
    {
        var cleanName = GetBaseName(orphan);

        // Already known: either the folder is in a record, or a record of the same name exists.
        if (_mods.Any(m => OwnsAFolderOf(m, orphan)) || _mods.Any(m => SameName(m, cleanName)))
            return new AdoptionOutcome(null, Array.Empty<Core.Deployment.AdoptionWarning>());

        var packageId = Guid.NewGuid().ToString("N")[..12];
        var plan = Core.Deployment.AdoptionPlanner.Build(
            packageId, cleanName, Describe(orphan), _index);

        if (plan.Components.Count == 0)
            return new AdoptionOutcome(null, plan.Warnings);

        var entry = new ModEntry
        {
            Id = packageId,
            Name = cleanName,
            InstalledPath = orphan.FullPath,
            IsEnabled = orphan.IsEnabled,
            ImportDate = DateTime.Now,
            Priority = _mods.Count > 0 ? _mods.Max(m => m.Priority) + 1 : 1
        };

        foreach (var folder in FoldersOf(orphan, TargetDomain.Dlc)) entry.GameDlcFolders.Add(folder);
        foreach (var folder in FoldersOf(orphan, TargetDomain.Mods)) entry.GameModFolders.Add(folder);

        var binComponent = plan.Components
            .FirstOrDefault(c => c.Domain == Core.Analysis.TargetDomain.Bin);
        foreach (var target in binComponent?.Targets ?? Array.Empty<Core.Deployment.TargetRecord>())
        {
            var relative = target.RelativePath;
            entry.GameBinFiles.Add(relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                ? relative[4..]
                : relative);
        }

        _mods.Add(entry);
        Save();

        // The index is what makes the folder recognisable later; adoption without it would leave the
        // same "no idea whose content this is" state the user is trying to get out of.
        UpdateIndex(index => Core.Deployment.LibraryIndexUpdater.WithPackage(
            index, entry.Id, entry.Name, plan.Components));

        return new AdoptionOutcome(entry, plan.Warnings);
    }

    /// <summary>What is on disk for this orphan, described the way the adoption planner wants it.</summary>
    private static IReadOnlyList<Core.Deployment.AdoptionComponent> Describe(OrphanModInfo orphan)
    {
        var components = new List<Core.Deployment.AdoptionComponent>();

        foreach (var (path, folderName, domain) in ComponentFolders(orphan))
        {
            components.Add(new Core.Deployment.AdoptionComponent(
                domain,
                folderName,
                Core.Deployment.DeployedContent.FolderFingerprint(path, "", domain) ?? "",
                BinFilesInside(path)));
        }

        return components;
    }

    /// <summary>
    /// The folder payloads of one orphan. A paired orphan is one package: the mod folder and the dlc
    /// folder it came with are recorded as two components of the same record.
    /// </summary>
    private static IEnumerable<(string Path, string Folder, Core.Analysis.TargetDomain Domain)> ComponentFolders(
        OrphanModInfo orphan)
    {
        foreach (var (folder, path, isDlc) in new[]
                 {
                     (orphan.FolderName, orphan.FullPath, orphan.IsInDlcFolder),
                     (orphan.PairedOrphan?.FolderName, orphan.PairedOrphan?.FullPath, orphan.PairedOrphan?.IsInDlcFolder ?? false)
                 })
        {
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(path)) continue;

            yield return (path!, folder!.StartsWith("~") ? folder[1..] : folder,
                isDlc ? Core.Analysis.TargetDomain.Dlc : Core.Analysis.TargetDomain.Mods);
        }
    }

    /// <summary>
    /// Bin files that came inside the adopted folder. Their content is recorded so ownership is
    /// known; nothing is remembered about what they replaced, so a later stop reports "no record"
    /// rather than guessing.
    /// </summary>
    private static IReadOnlyList<Core.Deployment.AdoptedFile> BinFilesInside(string folderPath)
    {
        var binRoot = Path.Combine(folderPath, "bin");
        if (!Directory.Exists(binRoot)) return Array.Empty<Core.Deployment.AdoptedFile>();

        var files = new List<Core.Deployment.AdoptedFile>();
        try
        {
            foreach (var file in Directory.GetFiles(binRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(binRoot, file).Replace('\\', '/');
                var hash = Core.Deployment.DeployedContent.FileHash(file);
                if (hash is null) continue;

                files.Add(new Core.Deployment.AdoptedFile(relative, hash));
            }
        }
        catch
        {
            // A folder that cannot be listed is adopted without its bin files rather than not at all.
        }

        return files;
    }

    /// <summary>True when a record already claims the folder this orphan is about.</summary>
    private static bool OwnsAFolderOf(ModEntry record, OrphanModInfo orphan)
    {
        var name = orphan.FolderName.StartsWith("~") ? orphan.FolderName[1..] : orphan.FolderName;
        var folders = orphan.IsInDlcFolder ? record.GameDlcFolders : record.GameModFolders;
        return folders.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameName(ModEntry record, string name) =>
        string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder names of one orphan that belong to a domain, paired folder included.</summary>
    private static IEnumerable<string> FoldersOf(OrphanModInfo orphan, Core.Analysis.TargetDomain domain)
    {
        foreach (var (folder, isDlc) in new[]
                 {
                     (orphan.FolderName, orphan.IsInDlcFolder),
                     (orphan.PairedOrphan?.FolderName, orphan.PairedOrphan?.IsInDlcFolder ?? false)
                 })
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            var wanted = domain == Core.Analysis.TargetDomain.Dlc;
            if (isDlc != wanted) continue;

            yield return folder!.StartsWith("~") ? folder[1..] : folder;
        }
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

    /// <summary>One record whose folders exist only in the game directory.</summary>
    public sealed record StorageCopyCandidate(
        string PackageId,
        string DisplayName,
        IReadOnlyList<Core.Deployment.StorageCopyItem> Items,
        long Bytes);

    public sealed record StorageCopySummary(
        int ModsCopied,
        int FoldersCopied,
        long BytesCopied,
        IReadOnlyList<string> Problems);

    public sealed record LibraryClearSummary(
        int RecordsCleared,
        IReadOnlyList<string> PreservedCopies,
        IReadOnlyList<string> Problems);

    /// <summary>
    /// Records with folders that are in the game directory but have no copy in storage. Exactly the
    /// state every adopted or earlier-installed package is in.
    /// </summary>
    public IReadOnlyList<StorageCopyCandidate> FindStorageCopyCandidates(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return Array.Empty<StorageCopyCandidate>();

        var candidates = new List<StorageCopyCandidate>();
        foreach (var mod in _mods)
        {
            var items = Core.Deployment.StorageCopyPlanner
                .Plan(mod.GameModFolders, Path.Combine(gamePath, "mods"), _modsDir)
                .Concat(Core.Deployment.StorageCopyPlanner
                    .Plan(mod.GameDlcFolders, Path.Combine(gamePath, "dlc"), _modsDir))
                .ToList();

            if (items.Count == 0) continue;

            candidates.Add(new StorageCopyCandidate(
                mod.Id, mod.Name, items,
                items.Sum(item => Measure(item.SourcePath).Bytes)));
        }

        return candidates;
    }

    /// <summary>
    /// Copies those folders into the storage directory. The game directory is only read: nothing is
    /// moved or deleted there, and an existing storage copy is never overwritten (the planner does
    /// not offer those folders in the first place).
    ///
    /// Each copy is checked against its source by file count and total size — cheap to compute and
    /// enough to catch a truncated copy, which is the failure that would otherwise only surface the
    /// day the game copy is gone.
    /// </summary>
    public StorageCopySummary CopyToStorage(IEnumerable<StorageCopyCandidate>? candidates)
    {
        var mods = 0;
        var folders = 0;
        long bytes = 0;
        var problems = new List<string>();

        foreach (var candidate in candidates ?? Array.Empty<StorageCopyCandidate>())
        {
            var copiedAny = false;
            foreach (var item in candidate.Items)
            {
                // Copy next to the target first: a failed or truncated copy must never be left where
                // a later run could mistake it for a finished one.
                var staging = item.TargetPath + ".w3mm-copy";
                try
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                    CopyDirectoryRecursive(item.SourcePath, staging);

                    var source = Measure(item.SourcePath);
                    var copied = Measure(staging);
                    if (source.Files != copied.Files || source.Bytes != copied.Bytes)
                    {
                        problems.Add(string.Format(
                            TranslationService.Instance.T("library.copy_mismatch"),
                            candidate.DisplayName + " / " + item.Folder,
                            source.Files, source.Bytes, copied.Files, copied.Bytes));
                        continue;
                    }

                    Directory.Move(staging, item.TargetPath);
                    folders++;
                    bytes += copied.Bytes;
                    copiedAny = true;
                }
                catch (Exception ex)
                {
                    problems.Add($"{candidate.DisplayName} / {item.Folder}: {ex.Message}");
                }
                finally
                {
                    TryDeleteDirectory(staging);
                }
            }

            if (copiedAny) mods++;
        }

        return new StorageCopySummary(mods, folders, bytes, problems);
    }

    /// <summary>
    /// Forgets every record. Only the library is cleared: the game directory, the storage copies and
    /// the backups stay exactly as they are — the user cleans those up themselves.
    ///
    /// The three documents are copied beside themselves first, so a decision made in a hurry is
    /// still reversible by hand. Nothing restores them automatically.
    /// </summary>
    public LibraryClearSummary ClearLibrary()
    {
        var (copies, problems) = Core.Deployment.LibraryReset.Preserve(new[]
        {
            _dbPath,
            Path.Combine(_dataDir, "library.json"),
            Path.Combine(_dataDir, "bin-ledger.json")
        }, DateTime.Now);

        var cleared = _mods.Count;
        _mods.Clear();
        Save();

        UpdateIndex(_ => Core.Deployment.LibraryIndex.Empty);
        UpdateBinClaims(_ => Core.Deployment.BinLedger.Empty);

        return new LibraryClearSummary(cleared, copies, problems);
    }

    /// <summary>Files and bytes under a folder, counted without reading any content.</summary>
    private static (int Files, long Bytes) Measure(string path)
    {
        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            return (files.Length, files.Sum(f =>
            {
                try { return new FileInfo(f).Length; }
                catch { return 0L; }
            }));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A leftover staging folder is reported by the next attempt, which reuses the same name.
        }
    }

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

        // What the index says is deployed, compared with what is there now. Read-only: the report
        // names the difference instead of "fixing" it, because the disk is the truth and a silent
        // repair would throw away whatever someone else put there.
        var drifts = hasGame
            ? Core.Deployment.DeploymentDrift.Find(_index, target => PresenceOf(gamePath!, target))
            : Array.Empty<Core.Deployment.TargetDrift>();

        return Core.Migration.LegacyLibraryAuditor.Audit(
            entries,
            folder => Directory.Exists(Path.Combine(_modsDir, folder)),
            hasGame
                ? (folder, isDlc) => Directory.Exists(Path.Combine(isDlc ? gameDlc! : gameMods!, folder))
                : null,
            drifts);
    }

    /// <summary>
    /// Whether a recorded target still holds what this manager wrote. Two details matter:
    ///
    /// - A folder renamed to its disabled form ("~name") counts as present: that is this manager's
    ///   own way of turning a mod off, not drift.
    /// - A folder's content is compared as a component fingerprint and a file's as a hash, using the
    ///   same computations the index was written with.
    /// </summary>
    private static Core.Deployment.TargetPresence PresenceOf(string gamePath, Core.Deployment.DeployedTarget deployed)
    {
        var relative = (deployed.Target.RelativePath ?? "").Replace('/', Path.DirectorySeparatorChar);
        if (relative.Length == 0) return Core.Deployment.TargetPresence.Missing;

        var full = Path.Combine(gamePath, relative);

        if (deployed.Target.Kind == Core.Deployment.TargetKind.File)
        {
            if (!File.Exists(full)) return Core.Deployment.TargetPresence.Missing;

            var hash = Core.Deployment.DeployedContent.FileHash(full);
            // A file that cannot be read is "cannot tell", and cannot-tell must never be presented
            // as evidence of a change.
            if (hash is null) return Core.Deployment.TargetPresence.Present;

            return string.Equals(hash, deployed.Target.WrittenHash, StringComparison.OrdinalIgnoreCase)
                ? Core.Deployment.TargetPresence.Present
                : Core.Deployment.TargetPresence.Modified;
        }

        if (Directory.Exists(full))
        {
            var fingerprint = Core.Deployment.DeployedContent.FolderFingerprint(
                full, deployed.PackageId, deployed.Domain);

            if (fingerprint is null) return Core.Deployment.TargetPresence.Present;

            return string.Equals(fingerprint, deployed.Target.WrittenHash, StringComparison.OrdinalIgnoreCase)
                ? Core.Deployment.TargetPresence.Present
                : Core.Deployment.TargetPresence.Modified;
        }

        var parent = Path.GetDirectoryName(full);
        var disabled = string.IsNullOrEmpty(parent)
            ? null
            : Path.Combine(parent, "~" + Path.GetFileName(full));

        return disabled is not null && Directory.Exists(disabled)
            ? Core.Deployment.TargetPresence.Present
            : Core.Deployment.TargetPresence.Missing;
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
