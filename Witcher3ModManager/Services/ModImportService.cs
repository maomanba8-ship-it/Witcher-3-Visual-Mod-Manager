using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Manifest;
using Witcher3ModManager.Models;
#if DEBUG
using System.Text.Json;
#endif

namespace Witcher3ModManager.Services;

public class ModImportService
{
    private readonly ModStorageService _storage;
    private readonly SettingsService _settings;
    private string _sevenZipPath;

    private static readonly string[] SevenZipCandidates =
    {
        @"D:\7-Zip\7z.exe",
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe"
    };
    /// <summary>
    /// Data model produced by the scan phase. Group a mod/dlc root with
    /// any nested folders of the opposite type (e.g. a mod containing a dlc).
    /// Build phase uses this to install everything without touching tempExtractDir.
    /// </summary>
    private class ModCandidate
    {
        public required DirectoryInfo Root;
        public readonly List<DirectoryInfo> AttachedDlcs = new();
        public readonly List<DirectoryInfo> AttachedMods = new();
    }


    public ModImportService(ModStorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;
        _sevenZipPath = Find7Zip();
    }

    /// <summary>
    /// [技术债] Import 流程重构 — BuildModEntry 只负责生成 ModEntry，禁止操作文件系统。
    /// ImportArchiveAsync 统一负责：解压、扫描、重名处理、Move、SaveMod、清理 temp。
    /// 当前 BuildModEntry 和 ImportArchiveAsync 的职责边界已模糊，请勿继续添加。


    /// [技术债] 中英文切换 — 当前 150+ 处硬编码中文，计划改为 Loc 字典 + 动态刷新，避免用 resx（防止 BAML 脱壳）。
    /// [技术债] bin/ 目录支持 — 部分 Mod 需要 bin\config\...\xxx.xml 复制到游戏 bin 目录，启动游戏菜单才能读取。
    /// 需要改动: ModEntry.GameBinFiles, ScanBinFiles(), EnableBin/DisableBin, ConflictService bin检测。
    /// 当前状态: bin 文件导入后无法在Mod 管理器里控制（游戏菜单读取）。
    /// 狮鹫多文件 / Bathhouse重复dlc / 重复导入 / 目录改名 等问题反复出现。
    /// </summary>
    public async Task<(List<ModEntry> Results, string? Error)> ImportArchiveAsync(string archivePath,
        IProgress<string>? progress = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found", archivePath);

        // Always re-resolve 7-Zip so a changed setting or bundled path is used without restart.
        RefreshSevenZipPath();

        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        progress?.Report(string.Format(TranslationService.Instance.T("import.progress.extracting"), Path.GetFileName(archivePath)));

        var tempExtractDir = Path.Combine(_storage.GetModsDirectory(), 
            "_tmp_" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(tempExtractDir))
            Directory.Delete(tempExtractDir, true);

        bool extracted = await Task.Run(() => ExtractArchive(archivePath, tempExtractDir));
        if (!extracted)
        {
            progress?.Report(TranslationService.Instance.T("import.progress.extract_failed"));
            return (new List<ModEntry>(), string.IsNullOrEmpty(_sevenZipPath) ? "7z_not_found" : "extract_failed");
        }

        var groupId = Guid.NewGuid().ToString("N")[..8];
        var results = new List<ModEntry>();
        string? errorCode = null;

        // Names the user confirmed to overwrite during pre-flight. Used to avoid a
        // second duplicate prompt when committing entries (same final storage name).
        var confirmedOverwriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Step 0: Detect bundle-patching mods (quickbms + .bms)
        if (IsBundlePatchMod(tempExtractDir))
        {
            progress?.Report(TranslationService.Instance.T("import.progress.bundle_unsupported"));
            try { Directory.Delete(tempExtractDir, true); } catch { }
            return (new List<ModEntry>(), "bundle_patch");
        }

        // Step 1: Scan for mod candidates (read-only, no file system changes)
        var modCandidates = ScanModCandidates(tempExtractDir);
        var binFiles = ScanBinFiles(tempExtractDir);
        // (S5 closure) Always-on read-only structure check. Guards only the truly
        // unrecognized package: when neither the analyzer nor the legacy scan finds a
        // single mod/dlc/bin component, abort with unknown_format instead of letting the
        // legacy FindModRoot guess-install an unverifiable package.
        var analysis = PackageAnalyzer.Analyze(tempExtractDir);
        if (analysis.RejectReason != null && modCandidates.Count == 0 && binFiles.Count == 0)
        {
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
            progress?.Report(TranslationService.Instance.T("import.progress.unrecognized"));
            return (new List<ModEntry>(), "unknown_format");
        }
        // (S5 closed-loop) If the analyzer flagged ambiguous components that need a user decision,
        // show them and require explicit confirmation BEFORE any filesystem write. Confident
        // packages (no decisions) keep the existing auto-import path unchanged.
        if (analysis.RequiresUserDecision.Count > 0)
        {
            var previewMsg = BuildImportPreviewMessage(analysis);
            var confirmed = Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    previewMsg,
                    TranslationService.Instance.T("import.preview_title"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) == MessageBoxResult.OK);
            if (!confirmed)
            {
                try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
                progress?.Report(TranslationService.Instance.T("import.cancelled"));
                return (new List<ModEntry>(), "cancelled");
            }
        }
        if (modCandidates.Count > 0)
        {
            // Parallel (read-only) analyzer comparison. Does NOT change old install flow.
#if DEBUG
            LogAnalyzerComparison(tempExtractDir);
#endif
            // Collect all mod and dlc roots (flatten ModCandidate)
            var modRoots = new List<DirectoryInfo>();
            var dlcRoots = new List<DirectoryInfo>();
            var allAttachedDlcs = new List<DirectoryInfo>();
            var allAttachedMods = new List<DirectoryInfo>();

            foreach (var cand in modCandidates)
            {
                bool isDlc = ClassifyRoot(cand.Root).IsDlc;
                if (isDlc)
                    dlcRoots.Add(cand.Root);
                else
                    modRoots.Add(cand.Root);

                foreach (var ad in cand.AttachedDlcs)
                    if (!allAttachedDlcs.Any(x => x.Name.Equals(ad.Name, StringComparison.OrdinalIgnoreCase)))
                        allAttachedDlcs.Add(ad);
                foreach (var am in cand.AttachedMods)
                    if (!allAttachedMods.Any(x => x.Name.Equals(am.Name, StringComparison.OrdinalIgnoreCase)))
                        allAttachedMods.Add(am);
            }

            // Merge all mod + dlc roots into a single ModEntry (package grouping)
            if (modRoots.Count > 0 || dlcRoots.Count > 0)
            {
                // Pre-discussion: detect duplicate planned storage names BEFORE any move/delete.
                // Cancel cleans temp and reports cancelled; confirm allows overwrite of listed targets only.
                var plannedNames = CollectPlannedStorageNames(modRoots, dlcRoots, allAttachedDlcs, allAttachedMods);
                var duplicateNames = FindExistingPlannedNames(plannedNames);
                if (!ResolveDuplicateTargetsBeforeWrite(duplicateNames, tempExtractDir, confirmedOverwriteNames, out var cancelErr))
                {
                    errorCode = cancelErr;
                    progress?.Report(TranslationService.Instance.T("import.cancelled"));
                    return (new List<ModEntry>(), errorCode);
                }

                // Record each component's own imported content while its bytes are still in
                // temp: a later import may delete and overwrite a shared storage folder, so
                // fingerprinting after the move would describe whichever package won.
                var componentFingerprints = CollectComponentFingerprints(
                    modRoots, allAttachedMods, dlcRoots, allAttachedDlcs);

                var primary = modRoots.Count > 0 ? modRoots[0] : dlcRoots[0];
                var primaryType = ClassifyRoot(primary).IsDlc ? ModType.Dlc : ModType.Mod;
                var entry = await BuildModEntry(primary.FullName, primaryType, groupId, archivePath, archiveName, progress, Path.GetFileName(primary.FullName));
                if (entry != null)
                {
                    entry.ComponentFingerprints = componentFingerprints;
                    entry.GameModFolders.Clear();
                    foreach (var mr in modRoots)
                        entry.GameModFolders.Add(mr.Name);

                    // Add attached mods (e.g. DLC root containing nested mods)
                    foreach (var am in allAttachedMods)
                        if (!entry.GameModFolders.Contains(am.Name))
                            entry.GameModFolders.Add(am.Name);

                    // DLC roots
                    if (dlcRoots.Count > 0)
                    {
                        entry.GameDlcFolders.Clear();
                        foreach (var dr in dlcRoots)
                            entry.GameDlcFolders.Add(dr.Name);

                        // Move dlc root folders to storage
                        foreach (var dr in dlcRoots)
                        {
                            if (!Directory.Exists(dr.FullName)) continue;
                            var dlcStorageDir = Path.Combine(_storage.GetModsDirectory(), dr.Name);
                            if (Directory.Exists(dlcStorageDir))
                                Directory.Delete(dlcStorageDir, true);
                            Directory.Move(dr.FullName, dlcStorageDir);
                        }
                    }

                    // Move attached DLCs to storage
                    foreach (var ad in allAttachedDlcs)
                    {
                        if (!entry.GameDlcFolders.Contains(ad.Name))
                            entry.GameDlcFolders.Add(ad.Name);
                        if (!Directory.Exists(ad.FullName)) continue;
                        var dlcStorageDir = Path.Combine(_storage.GetModsDirectory(), ad.Name);
                        if (Directory.Exists(dlcStorageDir))
                            Directory.Delete(dlcStorageDir, true);
                        Directory.Move(ad.FullName, dlcStorageDir);
                    }

                    // Move remaining mod roots to storage (primary already moved by BuildModEntry)
                    for (int mi = 1; mi < modRoots.Count; mi++)
                    {
                        var mr = modRoots[mi];
                        if (!Directory.Exists(mr.FullName)) continue;
                        var modStorageDir = Path.Combine(_storage.GetModsDirectory(), mr.Name);
                        if (Directory.Exists(modStorageDir))
                            Directory.Delete(modStorageDir, true);
                        if (Directory.Exists(mr.FullName))
                            Directory.Move(mr.FullName, modStorageDir);
                    }

                    // Move attached mods to storage
                    foreach (var am in allAttachedMods)
                    {
                        if (!Directory.Exists(am.FullName)) continue;
                        var modStorageDir = Path.Combine(_storage.GetModsDirectory(), am.Name);
                        if (Directory.Exists(modStorageDir))
                            Directory.Delete(modStorageDir, true);
                        if (Directory.Exists(am.FullName))
                            Directory.Move(am.FullName, modStorageDir);
                    }

                    // Attach bin files
                    if (binFiles.Count > 0)
                        AttachBinFiles(entry, tempExtractDir, binFiles);

                    results.Add(entry);
                }
            }
        }
        else
        {
            // No mod*/dlc* candidates — check for pure bin or try FindModRoot
            if (binFiles.Count > 0)
            {
                // Pure bin mod — no mod/dlc folders
                var entry = CreatePureBinEntry(archivePath, archiveName, groupId, tempExtractDir, binFiles);
                if (entry != null) results.Add(entry);
            }
            else
            {
                // Step 2: Fallback to FindModRoot
                var root = FindModRoot(tempExtractDir);
                if (root != null)
                {
                    var modType = Application.Current.Dispatcher.Invoke(() => AskModOrDlc(archiveName));
                    if (modType != null)
                    {
                        // Fallback path bypasses the candidate planner, but must still
                        // apply the same final-storage-name conflict gate before writing.
                        var targetName = EnsureModPrefix(Path.GetFileName(root), modType.Value);
                        var fbDuplicates = FindExistingPlannedNames(new List<string> { targetName });
                        if (!ResolveDuplicateTargetsBeforeWrite(fbDuplicates, tempExtractDir, confirmedOverwriteNames, out var fbCancel))
                        {
                            errorCode = fbCancel;
                            progress?.Report(TranslationService.Instance.T("import.cancelled"));
                            return (new List<ModEntry>(), errorCode);
                        }

                        var fallbackDomain = modType.Value == ModType.Dlc ? TargetDomain.Dlc : TargetDomain.Mods;
                        var fallbackFingerprint = ComponentFingerprintCollector.FromRoot(fallbackDomain, targetName, root);
                        var entry = await BuildModEntry(root, modType.Value, groupId, archivePath, archiveName, progress);
                        if (entry != null)
                        {
                            if (fallbackFingerprint != null)
                                entry.ComponentFingerprints.Add(fallbackFingerprint);
                            results.Add(entry);
                        }
                    }
                }
                else
                {
                    // Step 3: Can't identify
                    errorCode = "unknown_format";
                    progress?.Report(string.Format(TranslationService.Instance.T("import.progress.unrecognized"), archiveName));
                }
            }
        }

        // Cleanup temp
        try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }

        foreach (var entry in results)
        {
            #if DEBUG
            File.AppendAllText(@"Z:\griffin_import.log", $"json={JsonSerializer.Serialize(entry)}\r\n");
#endif
            if (_storage.AddMod(entry)) continue;

            // A duplicate key can only happen for a target already confirmed in
            // pre-flight (same final storage name). Replace silently — no second dialog.
            if (confirmedOverwriteNames.Contains(entry.GameModFolderName!))
            {
                _storage.AddOrReplaceMod(entry);
                continue;
            }

            // Safety-net fallback for unanticipated duplicates.
            var overwrite = Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    string.Format(TranslationService.Instance.T("import.duplicate_msg"), entry.Name),
                    TranslationService.Instance.T("import.duplicate_title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes);

            if (overwrite)
                _storage.AddOrReplaceMod(entry);
        }

        progress?.Report(results.Count > 0 
            ? string.Format(TranslationService.Instance.T("import.progress.done"), results.Count)
            : string.Format(TranslationService.Instance.T("import.progress.failed"), archiveName));
        
        return (results, errorCode);
    }

    /// <summary>
    /// Recursively scan for mod*/dlc* folders that directly contain a "content" subfolder.
    /// Deduplicates nested matches (keeps deepest).
    /// </summary>
    private static List<string> ScanBinFiles(string extractDir)
    {
        // Search recursively for bin/ directories (mods may nest bin inside a named folder)
        var binDirs = new DirectoryInfo(extractDir).GetDirectories("bin", SearchOption.AllDirectories);
        var results = new List<string>();
        foreach (var binDir in binDirs)
        {
            var files = Directory.GetFiles(binDir.FullName, "*.*", SearchOption.AllDirectories);
            results.AddRange(files.Select(f => f.Substring(binDir.FullName.Length + 1).Replace('\\', '/')));
        }
        return results;
    }
    /// <summary>
    /// Classify a directory as Mod or Dlc based on name prefix AND its container parent.
    /// A folder directly under a dlc/ or mods/ container is classified accordingly even
    /// when its name does not start with dlc/mod (e.g. dlc\MagicSpellsRevamped).
    /// Structural rule only - never a per-mod special case.
    /// </summary>
    private static (bool IsMod, bool IsDlc) ClassifyRoot(DirectoryInfo dir)
    {
        var name = dir.Name;
        var parent = dir.Parent?.Name ?? "";

        if (name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase))
            return (false, true);
        if (name.StartsWith("mod", StringComparison.OrdinalIgnoreCase))
            return (true, false);
        if (parent.Equals("dlc", StringComparison.OrdinalIgnoreCase))
            return (false, true);
        if (parent.Equals("mods", StringComparison.OrdinalIgnoreCase))
            return (true, false);
        return (false, false);
    }

    private static bool IsCandidateDir(DirectoryInfo d)
    {
        var (isMod, isDlc) = ClassifyRoot(d);
        if (!isMod && !isDlc) return false;
        // A component root must contain a recognized capability marker (content/metadata/scripts).
        return d.GetDirectories("content", SearchOption.TopDirectoryOnly).Length > 0 ||
               d.GetDirectories("metadata", SearchOption.TopDirectoryOnly).Length > 0 ||
               d.GetDirectories("scripts", SearchOption.TopDirectoryOnly).Length > 0;
    }

    /// <summary>
    /// Scan phase (read-only). Build ModCandidate objects grouping each mod/dlc root
    /// with any nested folders of the opposite type. Same-type nesting is deduplicated.
    /// Does NOT modify the file system.
    /// </summary>
    private static List<ModCandidate> ScanModCandidates(string extractDir)
    {
        var allDirs = new DirectoryInfo(extractDir).GetDirectories("*", SearchOption.AllDirectories);

        var allCandidates = allDirs.Where(IsCandidateDir).ToList();

        var result = new List<ModCandidate>();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Process shallowest first so outer containers are established before inner ones
        var sorted = allCandidates.OrderBy(d => d.FullName.Length).ToList();

        foreach (var cand in sorted)
        {
            if (processed.Contains(cand.FullName)) continue;

            // Check if contained by an already-registered candidate of the SAME type
            // (dedup: keep outermost)
            bool isDedup = false;
            foreach (var mc in result)
            {
                bool sameType = ClassifyRoot(cand).IsMod == ClassifyRoot(mc.Root).IsMod;
                if (sameType && cand.FullName.StartsWith(
                    mc.Root.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    isDedup = true;
                    break;
                }
            }
            if (isDedup) { processed.Add(cand.FullName); continue; }

            var mcNew = new ModCandidate { Root = cand };
            processed.Add(cand.FullName);

            // Scan for nested folders of the OPPOSITE type inside this root
            foreach (var inner in sorted)
            {
                if (processed.Contains(inner.FullName)) continue;
                if (!inner.FullName.StartsWith(
                    cand.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;

                bool sameType = ClassifyRoot(inner).IsMod == ClassifyRoot(cand).IsMod;
                if (sameType) continue; // same-type handled by dedup above

                if (ClassifyRoot(inner).IsDlc)
                    mcNew.AttachedDlcs.Add(inner);
                else
                    mcNew.AttachedMods.Add(inner);
                processed.Add(inner.FullName);
            }

            result.Add(mcNew);
        }

#if DEBUG
        File.AppendAllText(@"Z:\griffin_import.log", $"ScanModCandidates: {result.Count} root(s)\r\n");
        foreach (var mc in result)
        {
            File.AppendAllText(@"Z:\griffin_import.log", $"  Root: {mc.Root.FullName}\r\n");
            foreach (var ad in mc.AttachedDlcs)
                File.AppendAllText(@"Z:\griffin_import.log", $"    AttachedDlc: {ad.FullName}\r\n");
            foreach (var am in mc.AttachedMods)
                File.AppendAllText(@"Z:\griffin_import.log", $"    AttachedMod: {am.FullName}\r\n");
        }
#endif

        return result
            .OrderBy(mc => ClassifyRoot(mc.Root).IsDlc ? 1 : 0)
            .ThenBy(mc => mc.Root.Name)
            .ToList();
    }

    private async Task<ModEntry?> BuildModEntry(string modRoot, ModType modType, string groupId,
        string archivePath, string archiveName, IProgress<string>? progress, string? canonicalName = null)
    {
        progress?.Report(string.Format(TranslationService.Instance.T("import.progress.analyzing"), Path.GetFileName(modRoot)));

        var originalName = Path.GetFileName(modRoot);
        // Canonical deploy name: candidate-path roots are already container-typed (raw name is
        // the storage name); only the fallback bare-folder path prefixes here. One name is used
        // by planning, move and registration so record identity matches physical storage.
        var gameFolderName = canonicalName ?? EnsureModPrefix(originalName, modType);

        var finalModDir = Path.Combine(_storage.GetModsDirectory(), gameFolderName);

        // Overwrite semantics: duplicate targets are confirmed before this point,
        // so a still-existing directory is a confirmed overwrite target — clear it first.
        if (Directory.Exists(finalModDir))
            Directory.Delete(finalModDir, true);

        await Task.Run(() => MoveDirectory(modRoot, finalModDir));

        var modInfo = ParseModInfo(finalModDir);
        var category = DetectCategory(finalModDir, archiveName, modInfo);
        var screenshot = FindScreenshot(finalModDir, modInfo);

        var buildEntry = new ModEntry
        {
            Name = modInfo.TryGetValue("name", out var n) ? n : originalName,
            Author = modInfo.GetValueOrDefault("author", ""),
            Version = modInfo.GetValueOrDefault("version", ""),
            Description = modInfo.GetValueOrDefault("description", "") != ""
                ? modInfo.GetValueOrDefault("description", "")
                : ScanReadmeForImport(finalModDir),
            Category = category,
            ScreenshotPath = screenshot ?? "",
            SourceArchive = archivePath,
            InstalledPath = finalModDir,
            GroupId = groupId,
            ImportDate = DateTime.Now,
            IsEnabled = false,
            ModType = modType
        };

        // Authoritative deployment folder registration: a component goes to exactly one
        // bucket (mods/* for Mod, dlc/* for Dlc) so Enable/Disable/Delete never mix them.
        if (modType == ModType.Dlc)
            buildEntry.GameDlcFolderName = gameFolderName;
        else
            buildEntry.GameModFolderName = gameFolderName;

        return buildEntry;
    }

    /// <summary>
    /// Records one fingerprint per component root, keyed by its final storage folder name, so
    /// the owners of a shared folder can later be compared by (domain, folder). Read-only: it
    /// hashes whatever is on disk now and nothing else. Roots that are unreadable or hold no
    /// files are simply left unrecorded, and the reader must treat that as unknown history
    /// rather than as a match.
    /// </summary>
    private static List<ComponentFingerprintEntry> CollectComponentFingerprints(
        IEnumerable<DirectoryInfo> modRoots,
        IEnumerable<DirectoryInfo> attachedMods,
        IEnumerable<DirectoryInfo> dlcRoots,
        IEnumerable<DirectoryInfo> attachedDlcs)
    {
        var entries = new List<ComponentFingerprintEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(TargetDomain domain, DirectoryInfo root)
        {
            if (!seen.Add(domain + "\u0001" + root.Name)) return;
            var entry = ComponentFingerprintCollector.FromRoot(domain, root.Name, root.FullName);
            if (entry != null) entries.Add(entry);
        }

        foreach (var root in modRoots) Collect(TargetDomain.Mods, root);
        foreach (var root in attachedMods) Collect(TargetDomain.Mods, root);
        foreach (var root in dlcRoots) Collect(TargetDomain.Dlc, root);
        foreach (var root in attachedDlcs) Collect(TargetDomain.Dlc, root);
        return entries;
    }

    private static ModType? AskModOrDlc(string archiveName)
    {
        var result = MessageBox.Show(
            string.Format(TranslationService.Instance.T("import.ask_location_msg"), archiveName),
            TranslationService.Instance.T("import.ask_location_title"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => ModType.Mod,
            MessageBoxResult.No => ModType.Dlc,
            _ => null
        };
    }

private static string FindModRoot(string sourceDir)
    {
        var current = sourceDir;

        while (true)
        {
            var subDirs = Directory.GetDirectories(current);
            var files = Directory.GetFiles(current);

            // Root if: has "content" or "scripts" subfolder, or has modinfo.ini
            if (subDirs.Any(d =>
                    Path.GetFileName(d).Equals("content", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(d).Equals("scripts", StringComparison.OrdinalIgnoreCase)) ||
                files.Any(f => Path.GetFileName(f).Equals("modinfo.ini", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            // Exactly one subfolder, no files — drill in
            if (subDirs.Length == 1 && files.Length == 0)
            {
                current = subDirs[0];
                continue;
            }

            // Multiple items: check if exactly ONE subfolder looks like a mod root
            // (i.e., it contains content/scripts/modinfo.ini). If so, drill into it.
            // This handles archives with extra readme files at the top level.
            if (subDirs.Length >= 1 && (subDirs.Length > 1 || files.Length > 0))
            {
                var modCandidates = subDirs.Where(d =>
                {
                    var childSubs = Directory.GetDirectories(d);
                    var childFiles = Directory.GetFiles(d);
                    return childSubs.Any(c =>
                               Path.GetFileName(c).Equals("content", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetFileName(c).Equals("scripts", StringComparison.OrdinalIgnoreCase)) ||
                           childFiles.Any(f => Path.GetFileName(f).Equals("modinfo.ini", StringComparison.OrdinalIgnoreCase));
                }).ToArray();

                if (modCandidates.Length == 1)
                {
                    current = modCandidates[0];
                    continue;
                }
            }

            break;
        }

        return current;
    }

    /// <summary>
    /// Builds the S5 import-preview confirmation message from the analyzer result.
    /// </summary>
    private static string BuildImportPreviewMessage(AnalysisResult analysis)
    {
        var decisions = string.Join("\n", analysis.RequiresUserDecision);
        var components = string.Join("\n\n", analysis.Components.Select(c =>
        {
            var files = ManifestBuilder.ListFiles(c.RootPath);
            var head = files.Take(6).ToList();
            var fileList = head.Count > 0
                ? "\n      " + string.Join("\n      ", head)
                : "";
            var more = files.Count > head.Count
                ? $"\n      ... (+{files.Count - head.Count} more files)"
                : "";
            return $"{c.Name} ({c.Kind}) — {string.Join(", ", c.Evidence)}\n      {files.Count} file(s):{fileList}{more}";
        }));
        var warnings = analysis.Warnings.Count > 0
            ? "\n\n" + string.Join("\n", analysis.Warnings)
            : "";
        return string.Format(TranslationService.Instance.T("import.preview_body"), decisions, components) + warnings
             + "\n\n" + TranslationService.Instance.T("import.preview_ask");
    }

    /// <summary>
    /// Ensure the folder name starts with "mod" (or "dlc" for DLC) so the game recognizes it.
    /// </summary>
    private static string EnsureModPrefix(string name, ModType modType)
    {
        var prefix = modType == ModType.Dlc ? "dlc" : "mod";
        var clean = name.StartsWith("~") ? name[1..] : name;
        var lower = clean.ToLower();
        if (lower.StartsWith("mod") || lower.StartsWith("dlc"))
            return clean;
        return prefix + clean;
    }

private static string DeepFlatten(string sourceDir, string targetDir)
    {
        var current = sourceDir;

        while (true)
        {
            var subDirs = Directory.GetDirectories(current);
            var files = Directory.GetFiles(current);

            // If this folder has a "content" subfolder, it's a mod root — stop here
            if (subDirs.Any(d => string.Equals(Path.GetFileName(d), "content", StringComparison.OrdinalIgnoreCase)))
                break;

            // If there's exactly one subfolder and no files, drill into it
            if (subDirs.Length == 1 && files.Length == 0)
            {
                current = subDirs[0];
                continue;
            }

            // Multiple items or single item with files — this is likely the mod root
            break;
        }

        // If we drilled down, move the final folder to targetDir
        if (current != sourceDir)
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
            Directory.Move(current, targetDir);
            // Clean up the leftover temp dir chain
            if (Directory.Exists(sourceDir))
                Directory.Delete(sourceDir, true);
            return targetDir;
        }
        else
        {
            // Move sourceDir to targetDir
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
            Directory.Move(sourceDir, targetDir);
            return targetDir;
        }
    }

    private static ModType DetectModType(string modDir, string archiveName)
    {
        var dirName = Path.GetFileName(modDir);
        var searchText = (dirName + " " + archiveName).ToLower();

        if (dirName.StartsWith("dlc", StringComparison.OrdinalIgnoreCase) ||
            dirName.StartsWith("DLC", StringComparison.OrdinalIgnoreCase))
            return ModType.Dlc;

        var subDirs = Directory.GetDirectories(modDir);
        var hasDlcSubfolder = subDirs.Any(d =>
        {
            var name = Path.GetFileName(d);
            return name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase);
        });

        if (hasDlcSubfolder || searchText.Contains("dlc"))
            return ModType.Dlc;

        return ModType.Mod;
    }

    private bool ExtractArchive(string archivePath, string outputDir)
    {
        if (string.IsNullOrEmpty(_sevenZipPath))
            return false;

        try
        {
            Directory.CreateDirectory(outputDir);
            var psi = new ProcessStartInfo
            {
                FileName = _sevenZipPath,
                Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y -mmt=on",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(30000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private Dictionary<string, string> ParseModInfo(string modDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var candidates = Directory.GetFiles(modDir, "modinfo.ini", SearchOption.AllDirectories);
        if (candidates.Length == 0) return result;
        try
        {
            foreach (var line in File.ReadAllLines(candidates[0]))
            {
                var match = Regex.Match(line, @"^\s*(\w+)\s*=\s*(.+)\s*$");
                if (match.Success)
                    result[match.Groups[1].Value.Trim().ToLower()] = match.Groups[2].Value.Trim();
            }
        }
        catch { }
        return result;
    }

    private static CategoryType DetectCategory(string modDir, string archiveName,
        Dictionary<string, string> modInfo)
    {
        var category = modInfo.GetValueOrDefault("category", "").ToLower();
        var searchText = (Path.GetFileName(modDir) + " " + archiveName + " " + category).ToLower();

        if (category.Contains("geralt") || searchText.Contains("geralt")) return CategoryType.Geralt;
        if (category.Contains("yen") || searchText.Contains("yennefer") || searchText.Contains("yen")) return CategoryType.Yennefer;
        if (category.Contains("triss") || searchText.Contains("triss")) return CategoryType.Triss;
        if (category.Contains("graphics") || category.Contains("texture") ||
            searchText.Contains("texture") || searchText.Contains("graphic") ||
            searchText.Contains("reshade") || searchText.Contains("hd") ||
            searchText.Contains("lighting")) return CategoryType.Graphics;
        if (category.Contains("armor") || category.Contains("weapon") || category.Contains("sword") ||
            searchText.Contains("armor") || searchText.Contains("weapon") ||
            searchText.Contains("sword") || searchText.Contains("outfit") ||
            searchText.Contains("gear")) return CategoryType.ArmorWeapons;
        if (category.Contains("gameplay") || category.Contains("combat") || category.Contains("skill") ||
            searchText.Contains("gameplay") || searchText.Contains("combat") ||
            searchText.Contains("skill") || searchText.Contains("balance") ||
            searchText.Contains("overhaul")) return CategoryType.Gameplay;
        if (category.Contains("quest") || category.Contains("mission") ||
            searchText.Contains("quest") || searchText.Contains("mission")) return CategoryType.Quests;
        if (category.Contains("ui") || category.Contains("menu") || category.Contains("hud") ||
            searchText.Contains("ui") || searchText.Contains("menu") ||
            searchText.Contains("hud") || searchText.Contains("interface")) return CategoryType.UI;
        if (category.Contains("character") || category.Contains("npc") ||
            searchText.Contains("character") || searchText.Contains("npc") ||
            searchText.Contains("face") || searchText.Contains("body") ||
            searchText.Contains("model")) return CategoryType.Characters;

        return CategoryType.Other;
    }

    /// <summary>
    /// Detect bundle-patching mods that use quickbms to modify game .bundle files.
    /// These cannot be managed automatically.
    /// </summary>
    private static bool IsBundlePatchMod(string extractDir)
    {
        var files = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
        var hasQuickBms = files.Any(f => Path.GetFileName(f).Equals(
            "quickbms.exe", StringComparison.OrdinalIgnoreCase));
        var hasBmsScript = files.Any(f => Path.GetExtension(f).Equals(
            ".bms", StringComparison.OrdinalIgnoreCase));
        return hasQuickBms && hasBmsScript;
    }

    private string? FindScreenshot(string modDir, Dictionary<string, string> modInfo)
    {
        if (modInfo.TryGetValue("screenshot", out var ss))
        {
            var fullPath = Path.Combine(modDir, ss);
            if (File.Exists(fullPath)) return fullPath;
            var found = Directory.GetFiles(modDir, ss, SearchOption.AllDirectories);
            if (found.Length > 0) return found[0];
        }
        string[] commonNames = { "screenshot.png", "screenshot.jpg", "preview.png", "preview.jpg" };
        foreach (var name in commonNames)
        {
            var files = Directory.GetFiles(modDir, name, SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];
        }
        var images = Directory.GetFiles(modDir, "*.png", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(modDir, "*.jpg", SearchOption.AllDirectories))
            .Take(1).ToArray();
        return images.Length > 0 ? images[0] : null;
    }

    private string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "mod" : sanitized.Trim();
    }

    private string Find7Zip()
    {
        // 1. User-configured path (if still valid).
        var configured = _settings.Current.SevenZipPath;
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            return configured;

        // 2. Bundled 7-Zip next to the executable (matches Inno Setup layout).
        var bundled = Path.Combine(AppContext.BaseDirectory, "7zip", "7z.exe");
        if (File.Exists(bundled)) return bundled;

        // 3. Search system PATH.
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "7z",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                if (proc.ExitCode == 0)
                {
                    var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                        return firstLine;
                }
            }
        }
        catch { }

        // 4. Common install locations.
        foreach (var candidate in SevenZipCandidates)
            if (File.Exists(candidate)) return candidate;

        // 5. Nothing found.
        return "";
    }

    /// <summary>
    /// Re-resolve the 7-Zip path on each extraction, so a changed setting or a newly
    /// added bundled path is picked up without restarting the app.
    /// </summary>
    private void RefreshSevenZipPath()
    {
        _sevenZipPath = Find7Zip();
    }

    public bool IsSevenZipAvailable() => File.Exists(_sevenZipPath);
    public string GetSevenZipPath() => _sevenZipPath;

    private static string ScanReadmeForImport(string modDir)
    {
        string[] patterns = { "readme.txt", "README.txt", "readme.md", "README.md",
                              "description.txt", "info.txt", "modinfo.txt" };
        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(modDir, pattern, SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                try
                {
                    var text = File.ReadAllText(files[0]);
                    if (text.Length > 2000) text = text[..2000] + TranslationService.Instance.T("import.text_truncated");
                    return text;
                }
                catch { }
            }
        }
        var anyTxt = Directory.GetFiles(modDir, "*.txt", SearchOption.AllDirectories);
        if (anyTxt.Length > 0)
        {
            try
            {
                var text = File.ReadAllText(anyTxt[0]);
                if (text.Length > 2000) text = text[..2000] + TranslationService.Instance.T("import.text_truncated");
                return text;
            }
            catch { }
        }
        return "";
    }

    /// <summary>
    /// Create a ModEntry for a pure bin mod (no mod/dlc folders).
    /// </summary>
    private ModEntry? CreatePureBinEntry(string archivePath, string archiveName, string groupId,
        string extractDir, List<string> binFiles)
    {
        // Infer name from the parent of the bin/ directory (may be nested)
        var binDirs = new DirectoryInfo(extractDir).GetDirectories("bin", SearchOption.AllDirectories);
        var binRoot = binDirs.Length > 0 ? binDirs[0].FullName : Path.Combine(extractDir, "bin");
        var parentDir = new DirectoryInfo(binRoot).Parent;
        var inferredName = parentDir != null && !string.Equals(parentDir.Name, Path.GetFileName(extractDir), StringComparison.OrdinalIgnoreCase)
            ? parentDir.Name
            : new DirectoryInfo(extractDir).Name;

        var entry = new ModEntry
        {
            Name = inferredName,
            ModType = ModType.Mod,
            GroupId = groupId,
            InstalledPath = _storage.GetModsDirectory(),
            IsEnabled = false
        };

        AttachBinFiles(entry, extractDir, binFiles);
        return entry;
    }

    /// <summary>
    /// Copy bin files from extract directory to mod storage and populate GameBinFiles.
    /// </summary>
    private void AttachBinFiles(ModEntry entry, string extractDir, List<string> binFiles)
    {
        if (binFiles.Count == 0) return;

        // Find actual bin directory (may be nested: ModName/bin/..., not just bin/...)
        var binDirs = new DirectoryInfo(extractDir).GetDirectories("bin", SearchOption.AllDirectories);
        var binRoot = binDirs.Length > 0 ? binDirs[0].FullName : Path.Combine(extractDir, "bin");

        var binStorageDir = _storage.GetModBinDirectory(entry.Id);
        entry.BinStoragePath = binStorageDir;
        entry.GameBinFiles.AddRange(binFiles);

        foreach (var bf in binFiles)
        {
            var src = Path.Combine(binRoot, bf.Replace('/', Path.DirectorySeparatorChar));
            var dest = Path.Combine(binStorageDir, bf.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (File.Exists(src)) File.Copy(src, dest, true);
        }
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

    /// <summary>
    /// Collect the storage directory names this import plans to write into (mod roots,
    /// dlc roots, attached dlc/mod fetches). Used to detect duplicates before any move/delete.
    /// </summary>
    private List<string> CollectPlannedStorageNames(
        IReadOnlyList<DirectoryInfo> modRoots,
        IReadOnlyList<DirectoryInfo> dlcRoots,
        IReadOnlyList<DirectoryInfo> attachedDlcs,
        IReadOnlyList<DirectoryInfo> attachedMods)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Candidate-path components are already container-typed; their raw root name is the
        // canonical storage name (matches BuildModEntry canonicalName). Bare "Foo -> modFoo"
        // is handled by the fallback path, which prefixes explicitly.
        for (int i = 0; i < modRoots.Count; i++)
            names.Add(modRoots[i].Name);
        foreach (var dr in dlcRoots) names.Add(dr.Name);
        foreach (var ad in attachedDlcs) names.Add(ad.Name);
        foreach (var am in attachedMods) names.Add(am.Name);
        return names.ToList();
    }

    /// <summary>
    /// Returns the subset of planned storage names that already exist either as a physical
    /// directory under storage, or as a name already referenced by a stored mod/dlc entry.
    /// </summary>
    private List<string> FindExistingPlannedNames(List<string> plannedNames)
    {
        var existing = new List<string>();
        var modsDir = _storage.GetModsDirectory();
        var records = _storage.GetAllMods();

        foreach (var name in plannedNames)
        {
            bool physical = Directory.Exists(Path.Combine(modsDir, name));
            bool referenced = records.Any(r =>
                (r.GameModFolders != null && r.GameModFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) ||
                (r.GameDlcFolders != null && r.GameDlcFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) ||
                string.Equals(r.GameModFolderName, name, StringComparison.OrdinalIgnoreCase));

            if (physical || referenced) existing.Add(name);
        }
        return existing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Pre-discussion guard: if any planned storage name already exists, ask the user once.
    /// Returns true when it is safe to proceed (user confirmed overwrite, or no conflicts).
    /// On cancel it cleans the temp dir and reports the caller.
    /// </summary>
    private bool ResolveDuplicateTargetsBeforeWrite(
        List<string> duplicateNames,
        string tempExtractDir,
        HashSet<string> confirmedOverwriteNames,
        out string? cancelReason)
    {
        cancelReason = null;
        if (duplicateNames.Count == 0) return true;

        var overwrite = Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(
                string.Format(TranslationService.Instance.T("import.overwrite_msg"), string.Join(", ", duplicateNames)),
                TranslationService.Instance.T("import.overwrite_title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);

        if (!overwrite)
        {
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
            cancelReason = "cancelled";
            return false;
        }

        // Overwrite confirmed once: remove existing physical targets so downstream
        // deployment reuses the canonical name (no timestamp suffix), and record the
        // names so the DB write can replace silently without a second prompt.
        foreach (var name in duplicateNames)
        {
            var dir = Path.Combine(_storage.GetModsDirectory(), name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            confirmedOverwriteNames.Add(name);
        }
        return true;
    }

#if DEBUG
    private static void LogAnalyzerComparison(string extractDir)
    {
        try
        {
            var result = PackageAnalyzer.Analyze(extractDir);
            File.AppendAllText(@"Z:\griffin_import.log", $"[Analyzer] components={result.Components.Count} warnings={result.Warnings.Count}\r\n");
            foreach (var comp in result.Components)
                File.AppendAllText(@"Z:\griffin_import.log", $"  {comp.Kind}: {comp.Name} ({comp.RootPath}) evidence={string.Join(",", comp.Evidence)}\r\n");
        }
        catch { }
    }
#endif
}
