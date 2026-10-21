using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Import;
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

        // Step 1: read the package structure (read-only). The analyzer decides what the package
        // contains; the legacy folder-name scan still runs in DEBUG only, to compare conclusions.
        var analysis = PackageAnalyzer.Analyze(tempExtractDir);
        var candidates = Core.Import.ImportCandidates.From(analysis);

        // Flat list of the bin files this package would deploy, for the checks that compare against
        // what other packages already have.
        var binFiles = candidates.BinRoots
            .SelectMany(Core.Import.ImportCandidates.RelativePathsUnder)
            .ToList();

#if DEBUG
        LogCandidateComparison(
            Core.Import.LegacyCandidateScanner.Scan(tempExtractDir),
            Core.Import.LegacyCandidateScanner.ScanBinFiles(tempExtractDir),
            analysis);
#endif

        if (analysis.RejectReason != null && candidates.IsEmpty)
        {
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
            progress?.Report(TranslationService.Instance.T("import.progress.unrecognized"));
            return (new List<ModEntry>(), "unknown_format");
        }
        // Two bin roots inside this package that write the same relative path would lose a file
        // silently once they are merged, so this is refused rather than resolved by picking one.
        var internalBinCollisions = Core.Import.ImportCandidates.BinPathCollisions(
            candidates.BinRoots.Select(root => (Root: root,
                RelativePaths: (IEnumerable<string>)Core.Import.ImportCandidates.RelativePathsUnder(root))));

        if (internalBinCollisions.Count > 0)
        {
            var message = string.Format(
                TranslationService.Instance.T("import.bin_collision_internal"),
                string.Join("\n", internalBinCollisions.Select(p => "bin/" + p)));
            Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                message,
                TranslationService.Instance.T("import.bin_collision_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning));

            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
            progress?.Report(TranslationService.Instance.T("import.cancelled"));
            return (new List<ModEntry>(), "bin_collision");
        }

        // A bin file another package already deploys is a conflict the user has to agree to before
        // anything is written. Storage cannot show it: each package keeps its own copy, and the two
        // only meet in the game's bin directory. This runs before the folder pre-flight below so it
        // covers bin-only packages too — those have no folders at all, and used to skip every check.
        var binCollisions = FindBinCollisions(binFiles);
        if (binCollisions.Count > 0 && !ConfirmBinCollisions(binCollisions))
        {
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
            progress?.Report(TranslationService.Instance.T("import.cancelled"));
            return (new List<ModEntry>(), "cancelled");
        }

        if (candidates.Mods.Count > 0 || candidates.Dlcs.Count > 0)
        {
            // Parallel (read-only) analyzer comparison. Does NOT change old install flow.
#if DEBUG
            LogAnalyzerComparison(tempExtractDir);
#endif
            var modRoots = candidates.Mods;
            var dlcRoots = candidates.Dlcs;

                // Pre-discussion: detect duplicate planned storage names BEFORE any move/delete.
                // Cancel cleans temp and reports cancelled; confirm allows overwrite of listed targets only.
                var plannedNames = modRoots.Select(c => c.Name)
                    .Concat(dlcRoots.Select(c => c.Name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var duplicateNames = FindExistingPlannedNames(plannedNames);

                // One read-only preview of the whole import: what will be created, what will be
                // replaced, and anything the analyzer was unsure about. Nothing has been written
                // at this point, so cancelling leaves storage, the game directory and the records
                // untouched.
                // The preview describes what is actually going to be installed: components the
                // adapter dropped (a folder with no files) must not appear as "to be added".
                var planned = new AnalysisResult(
                    modRoots.Cast<PackageComponent>()
                        .Concat(dlcRoots)
                        .Concat(analysis.Components.Where(c => c.Kind == ComponentKind.Bin))
                        .ToList(),
                    analysis.Warnings,
                    analysis.RequiresUserDecision,
                    analysis.RejectReason);

                var plan = ImportPlanner.Build(planned, duplicateNames, component => component.Name);
                if (plan.RequiresConfirmation)
                {
                    var previewMsg = BuildImportPlanPreview(plan);
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

                if (!ResolveDuplicateTargetsBeforeWrite(duplicateNames, tempExtractDir, confirmedOverwriteNames, plan.RequiresConfirmation, out var cancelErr))
                {
                    errorCode = cancelErr;
                    progress?.Report(TranslationService.Instance.T("import.cancelled"));
                    return (new List<ModEntry>(), errorCode);
                }

                // Record each component's own imported content while its bytes are still in temp: a
                // later import may delete and overwrite a shared storage folder, so fingerprinting
                // after the move would describe whichever package won.
                var componentFingerprints = CollectComponentFingerprints(modRoots, dlcRoots);

                var primary = candidates.Primary!;
                var primaryType = primary.Kind == ComponentKind.Dlc ? ModType.Dlc : ModType.Mod;
                var entry = await BuildModEntry(primary.RootPath, primaryType, groupId, archivePath, archiveName, progress, primary.Name);
                if (entry != null)
                {
                    entry.ComponentFingerprints = componentFingerprints;

                    entry.GameModFolders.Clear();
                    foreach (var component in modRoots)
                        entry.GameModFolders.Add(component.Name);

                    entry.GameDlcFolders.Clear();
                    foreach (var component in dlcRoots)
                        entry.GameDlcFolders.Add(component.Name);

                    // The record is built, so every other root goes into storage. The primary is
                    // already there, which the existence check skips.
                    foreach (var component in modRoots.Concat(dlcRoots)
                                 .Where(c => !IsSamePath(c.RootPath, primary.RootPath)))
                        MoveComponentToStorage(component.RootPath);

                    if (binFiles.Count > 0)
                        AttachBinFiles(entry, candidates.BinRoots);

                    results.Add(entry);
                }
            }
        else
        {
            // No mod/dlc components — a pure bin package, or something to ask about by hand.
            if (candidates.BinRoots.Count > 0)
            {
                // Pure bin mod — no mod/dlc folders
                var entry = CreatePureBinEntry(archivePath, archiveName, groupId, tempExtractDir, candidates.BinRoots);
                if (entry != null) results.Add(entry);
            }
            else
            {
                // Nothing identifiable. Ask once, explicitly, instead of guessing: install the
                // whole tree as a mod or a dlc, or do not install it at all.
                var manualRoot = Core.Import.ManualInstallRoot.From(tempExtractDir);
                if (manualRoot is null)
                {
                    errorCode = "unknown_format";
                    progress?.Report(string.Format(TranslationService.Instance.T("import.progress.unrecognized"), archiveName));
                }
                else
                {
                    var modType = Application.Current.Dispatcher.Invoke(() => AskModOrDlc(archiveName));
                    if (modType is null)
                    {
                        try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
                        progress?.Report(TranslationService.Instance.T("import.cancelled"));
                        return (new List<ModEntry>(), "cancelled");
                    }

                    // The whole extraction tree has no name of its own, so the archive names it.
                    var nameFromArchive = IsSamePath(manualRoot, tempExtractDir);
                    var targetName = EnsureModPrefix(
                        nameFromArchive ? archiveName : Path.GetFileName(manualRoot), modType.Value);

                    // Manual installs answer their own prompt, but still go through the same
                    // final-storage-name conflict gate before anything is written.
                    var duplicates = FindExistingPlannedNames(new List<string> { targetName });
                    if (!ResolveDuplicateTargetsBeforeWrite(duplicates, tempExtractDir, confirmedOverwriteNames, alreadyConfirmed: false, out var manualCancel))
                    {
                        errorCode = manualCancel;
                        progress?.Report(TranslationService.Instance.T("import.cancelled"));
                        return (new List<ModEntry>(), errorCode);
                    }

                    var domain = modType.Value == ModType.Dlc ? TargetDomain.Dlc : TargetDomain.Mods;
                    var fingerprint = ComponentFingerprintCollector.FromRoot(domain, targetName, manualRoot);
                    var entry = await BuildModEntry(manualRoot, modType.Value, groupId, archivePath, archiveName, progress, targetName);
                    if (entry != null)
                    {
                        if (fingerprint != null) entry.ComponentFingerprints.Add(fingerprint);
                        results.Add(entry);
                    }
                }
            }
        }

        // Cleanup temp
        try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }

        foreach (var entry in results)
        {
#if DEBUG
            DiagnosticLog.Write($"json={JsonSerializer.Serialize(entry)}");
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
        IEnumerable<PackageComponent> mods,
        IEnumerable<PackageComponent> dlcs)
    {
        var entries = new List<ComponentFingerprintEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(TargetDomain domain, PackageComponent component)
        {
            if (!seen.Add(domain + "\u0001" + component.Name)) return;
            var entry = ComponentFingerprintCollector.FromRoot(domain, component.Name, component.RootPath);
            if (entry != null) entries.Add(entry);
        }

        foreach (var component in mods) Collect(TargetDomain.Mods, component);
        foreach (var component in dlcs) Collect(TargetDomain.Dlc, component);
        return entries;
    }

    /// <summary>
    /// Moves one component root into the storage directory, replacing an existing target (those
    /// were confirmed by the duplicate pre-flight). A root that is no longer there is skipped: the
    /// primary was already moved while its record was built.
    /// </summary>
    private void MoveComponentToStorage(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return;

        var target = Path.Combine(_storage.GetModsDirectory(), Path.GetFileName(rootPath));
        if (Directory.Exists(target)) Directory.Delete(target, true);
        if (Directory.Exists(rootPath)) Directory.Move(rootPath, target);
    }

    private static bool IsSamePath(string? left, string? right) =>
        string.Equals(
            (left ?? "").TrimEnd('\\', '/'),
            (right ?? "").TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the analyzer's grouping beside the legacy scanner and reports where the two disagree.
    ///
    /// Nothing here decides anything: the container path keeps using the old scanner until this
    /// comparison comes up clean on the fixtures and on real packages. That is the point of this
    /// stage — the switch has to be observed before it is made.
    /// </summary>
    private static void LogCandidateComparison(
        List<Core.Import.LegacyCandidate> legacyCandidates,
        List<string> legacyBinFiles,
        AnalysisResult analysis)
    {
        var byAnalysis = Core.Import.ImportCandidates.From(analysis);

        var legacyMods = legacyCandidates
            .SelectMany(candidate => new[] { candidate.Root }.Concat(candidate.AttachedMods))
            .Where(dir => !Core.Import.LegacyCandidateScanner.Classify(dir).IsDlc)
            .Select(dir => dir.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var legacyDlcs = legacyCandidates
            .SelectMany(candidate => new[] { candidate.Root }.Concat(candidate.AttachedDlcs))
            .Where(dir => Core.Import.LegacyCandidateScanner.Classify(dir).IsDlc)
            .Select(dir => dir.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var analysisMods = byAnalysis.Mods.Select(c => c.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        var analysisDlcs = byAnalysis.Dlcs.Select(c => c.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        var binRoots = byAnalysis.BinRoots
            .Select(root => (Root: root,
                RelativePaths: (IEnumerable<string>)Core.Import.ImportCandidates.RelativePathsUnder(root)))
            .ToList();
        var analysisBinFiles = binRoots.Sum(root => root.RelativePaths.Count());

        DiagnosticLog.Write(
            $"[candidates] legacy mods=[{string.Join(", ", legacyMods)}] " +
            $"dlcs=[{string.Join(", ", legacyDlcs)}] binFiles={legacyBinFiles.Count}");
        DiagnosticLog.Write(
            $"[candidates] analysis mods=[{string.Join(", ", analysisMods)}] " +
            $"dlcs=[{string.Join(", ", analysisDlcs)}] binRoots={binRoots.Count} binFiles={analysisBinFiles}");

        ReportDifference("mods", legacyMods, analysisMods);
        ReportDifference("dlcs", legacyDlcs, analysisDlcs);

        foreach (var warning in byAnalysis.Warnings)
            DiagnosticLog.Write($"[candidates] analysis warning: {warning}");

        foreach (var path in Core.Import.ImportCandidates.BinPathCollisions(binRoots))
            DiagnosticLog.Write($"[candidates] bin path claimed by more than one root: {path}");
    }

    private static void ReportDifference(
        string what,
        IReadOnlyList<string> legacy,
        IReadOnlyList<string> analysis)
    {
        var onlyLegacy = legacy.Except(analysis, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyAnalysis = analysis.Except(legacy, StringComparer.OrdinalIgnoreCase).ToList();

        if (onlyLegacy.Count > 0)
            DiagnosticLog.Write($"[candidates] {what} only in the legacy scan: {string.Join(", ", onlyLegacy)}");
        if (onlyAnalysis.Count > 0)
            DiagnosticLog.Write($"[candidates] {what} only in the analysis: {string.Join(", ", onlyAnalysis)}");
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


    /// <summary>
    /// Renders the read-only import plan for the confirmation dialog: what will be created, what
    /// will be replaced, anything that cannot be placed automatically, and an explicit reminder
    /// that nothing has been written yet. The deployment rules themselves are unchanged; this
    /// only describes what the existing flow is about to do.
    /// </summary>
    private static string BuildImportPlanPreview(ImportPlan plan)
    {
        var sections = new List<string>();

        AddPlanSection(sections, "import.plan_create",
            plan.Components.Where(c => c.Action == ImportAction.Create));
        AddPlanSection(sections, "import.plan_overwrite", plan.Overwrites);
        AddPlanSection(sections, "import.plan_unsupported",
            plan.Components.Where(c => c.Action == ImportAction.Unsupported));

        if (plan.Decisions.Count > 0)
            sections.Add(string.Join("\n", plan.Decisions));

        if (plan.Warnings.Count > 0)
            sections.Add(TranslationService.Instance.T("import.plan_warnings") + "\n" + string.Join("\n", plan.Warnings));

        sections.Add(TranslationService.Instance.T("import.plan_safe_cancel"));

        return string.Join("\n\n", sections) + "\n\n" + TranslationService.Instance.T("import.preview_ask");
    }

    private static void AddPlanSection(
        List<string> sections,
        string titleKey,
        IEnumerable<ImportPlanComponent> components)
    {
        var list = components.ToList();
        if (list.Count == 0) return;

        var lines = list.Select(component =>
        {
            var files = ManifestBuilder.ListFiles(component.RootPath);
            var examples = files.Take(3).ToList();
            var exampleText = examples.Count > 0 ? "\n      " + string.Join("\n      ", examples) : "";
            var moreText = files.Count > examples.Count
                ? $"\n      ... (+{files.Count - examples.Count} more)"
                : "";
            return $"  • {component.Name} ({component.Kind}) → {component.DeployName} — {files.Count} file(s){exampleText}{moreText}";
        });

        sections.Add(TranslationService.Instance.T(titleKey) + "\n" + string.Join("\n", lines));
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
    /// Bin files this import would write that an existing record already deploys. The comparison is
    /// on the paths inside the game's bin directory, because that is where two packages can meet.
    /// </summary>
    private IReadOnlyList<Core.Import.BinCollision> FindBinCollisions(IReadOnlyList<string> plannedBinFiles)
    {
        if (plannedBinFiles.Count == 0) return Array.Empty<Core.Import.BinCollision>();

        var deployed = _storage.GetAllMods()
            .Where(m => (m.GameBinFiles?.Count ?? 0) > 0)
            .Select(m => (Owner: m.DisplayName, Paths: (IEnumerable<string>?)m.GameBinFiles))
            .ToList();

        return Core.Import.BinCollisionFinder.Find(plannedBinFiles, deployed);
    }

    /// <summary>
    /// Asks before importing a package that overlaps an installed one's bin files. Nothing has been
    /// written at this point, so cancelling leaves storage, the game directory and the records
    /// exactly as they were.
    /// </summary>
    private static bool ConfirmBinCollisions(IReadOnlyList<Core.Import.BinCollision> collisions)
    {
        var lines = collisions.Select(c => string.Format(
            TranslationService.Instance.T("import.bin_collision_line"),
            "bin/" + c.Path,
            string.Join(", ", c.Owners)));

        var message = string.Format(
            TranslationService.Instance.T("import.bin_collision_body"),
            string.Join("\n", lines));

        return Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
            message,
            TranslationService.Instance.T("import.bin_collision_title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK);
    }

    /// <summary>
    /// Create a ModEntry for a pure bin mod (no mod/dlc folders).
    /// </summary>
    private ModEntry? CreatePureBinEntry(string archivePath, string archiveName, string groupId,
        string extractDir, IReadOnlyList<string> binRoots)
    {
        if (binRoots.Count == 0) return null;

        // Infer name from the parent of the first bin/ root (may be nested).
        var binRoot = binRoots[0];
        var parentDir = new DirectoryInfo(binRoot).Parent;
        // The extraction directory is this manager's own temporary folder, so it is not a name: the
        // rule lives in Core where it can be tested without an archive.
        var inferredName = Witcher3ModManager.Core.Analysis.BinOnlyPackageName.From(
            parentDir?.Name, new DirectoryInfo(extractDir).Name, archiveName);

        var entry = new ModEntry
        {
            Name = inferredName,
            ModType = ModType.Mod,
            GroupId = groupId,
            InstalledPath = _storage.GetModsDirectory(),
            IsEnabled = false
        };

        AttachBinFiles(entry, binRoots);
        return entry;
    }

    /// <summary>
    /// Copies the package's bin files into its storage folder, each from the root it actually came
    /// from. The old shape took the first bin/ directory and used it for every file, which is how a
    /// second root's files ended up copied from the wrong place.
    /// </summary>
    private void AttachBinFiles(ModEntry entry, IReadOnlyList<string> binRoots)
    {
        if (binRoots.Count == 0) return;

        var binStorageDir = _storage.GetModBinDirectory(entry.Id);
        var copied = 0;

        foreach (var root in binRoots)
        {
            foreach (var relative in Core.Import.ImportCandidates.RelativePathsUnder(root))
            {
                var source = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source)) continue;

                var target = Path.Combine(binStorageDir, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);

                if (!entry.GameBinFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    entry.GameBinFiles.Add(relative);
                copied++;
            }
        }

        // Only claim a storage folder when something actually went into it.
        if (copied > 0) entry.BinStoragePath = binStorageDir;
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
        bool alreadyConfirmed,
        out string? cancelReason)
    {
        cancelReason = null;
        if (duplicateNames.Count == 0) return true;

        // The import plan preview already put this question to the user, so do not ask twice.
        // Already-confirmed names still fall through to the bookkeeping below.
        if (!alreadyConfirmed)
        {
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
            DiagnosticLog.Write($"[Analyzer] components={result.Components.Count} warnings={result.Warnings.Count}");
            foreach (var comp in result.Components)
                DiagnosticLog.Write($"  {comp.Kind}: {comp.Name} ({comp.RootPath}) evidence={string.Join(",", comp.Evidence)}");
        }
        catch { }
    }
#endif
}
