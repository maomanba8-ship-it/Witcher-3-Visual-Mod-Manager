using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Witcher3ModManager.Models;
#if DEBUG
using System.Text.Json;
#endif

namespace Witcher3ModManager.Services;

public class ModImportService
{
    private readonly ModStorageService _storage;
    private readonly SettingsService _settings;
    private readonly string _sevenZipPath;

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

        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        progress?.Report($"正在解压: {Path.GetFileName(archivePath)}...");

        var tempExtractDir = Path.Combine(_storage.GetModsDirectory(), 
            "_tmp_" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(tempExtractDir))
            Directory.Delete(tempExtractDir, true);

        bool extracted = await Task.Run(() => ExtractArchive(archivePath, tempExtractDir));
        if (!extracted)
        {
            progress?.Report("解压失败");
            return (new List<ModEntry>(), string.IsNullOrEmpty(_sevenZipPath) ? "7z_not_found" : "extract_failed");
        }

        var groupId = Guid.NewGuid().ToString("N")[..8];
        var results = new List<ModEntry>();
        string? errorCode = null;

        // Step 0: Detect bundle-patching mods (quickbms + .bms)
        if (IsBundlePatchMod(tempExtractDir))
        {
            progress?.Report("不支持打包模块：Bundle 补丁模块请手动运行 Install.bat");
            try { Directory.Delete(tempExtractDir, true); } catch { }
            return (new List<ModEntry>(), "bundle_patch");
        }

        // Step 1: Recursively scan for mod*/dlc* folders with content/
        var candidates = ScanModDlcRoots(tempExtractDir);
        var binFiles = ScanBinFiles(tempExtractDir);

        if (candidates.Count > 0)
        {
            // Separate mod and dlc candidates
            var modDirs = candidates.Where(c => c.Name.StartsWith("mod", StringComparison.OrdinalIgnoreCase)).ToList();
#if DEBUG
            File.AppendAllText(@"Z:\griffin_import.log", $"mods={string.Join(",", modDirs.Select(x => x.Name))}\r\n");
#endif
            var dlcDirs = candidates.Where(c => c.Name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase)).ToList();
            var other = candidates.FirstOrDefault(c => !c.Name.StartsWith("mod", StringComparison.OrdinalIgnoreCase)
                                                     && !c.Name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase));

            if (modDirs.Count > 0 || dlcDirs.Count > 0)
            {
                // Merge into single ModEntry
                var primary = modDirs.Count > 0 ? modDirs[0] : dlcDirs[0];
                var entry = await BuildModEntry(primary.FullName, ModType.Mod, groupId, archivePath, archiveName, progress);
                if (entry != null)
                {
                    entry.GameModFolders.Clear(); foreach (var md in modDirs) entry.GameModFolders.Add(md.Name);
#if DEBUG
                    File.AppendAllText(@"Z:\griffin_import.log", $"entry={string.Join(",", entry.GameModFolders)}\r\n");
#endif
                    if (dlcDirs.Count > 0)
                    {
                        entry.GameDlcFolders.Clear(); foreach (var dd in dlcDirs) entry.GameDlcFolders.Add(dd.Name);
                        // Also move dlc folder to storage
                        foreach (var dd in dlcDirs) { if (!Directory.Exists(dd.FullName)) continue; var dlcStorageDir = Path.Combine(_storage.GetModsDirectory(), dd.Name);
                        if (Directory.Exists(dlcStorageDir))
                            Directory.Delete(dlcStorageDir, true);
                        Directory.Move(dd.FullName, dlcStorageDir); }
                    }
                    // Move remaining mod folders to storage (primary already moved by BuildModEntry)
                    for (int mi = 1; mi < modDirs.Count; mi++)
                    {
                        var md = modDirs[mi]; if (!Directory.Exists(md.FullName)) continue; var modStorageDir = Path.Combine(_storage.GetModsDirectory(), md.Name);
                        if (Directory.Exists(modStorageDir))
                            Directory.Delete(modStorageDir, true);
                        if (Directory.Exists(md.FullName))
                            Directory.Move(md.FullName, modStorageDir);
                    }

                    // Attach bin files
                    if (binFiles.Count > 0)
                        AttachBinFiles(entry, tempExtractDir, binFiles);
                    results.Add(entry);
                }
            }
            if (other != null)
            {
                var entry = await BuildModEntry(other.FullName, ModType.Mod, groupId, archivePath, archiveName, progress);
                if (entry != null) results.Add(entry);
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
                        var entry = await BuildModEntry(root, modType.Value, groupId, archivePath, archiveName, progress);
                        if (entry != null) results.Add(entry);
                    }
                }
                else
                {
                    // Step 3: Can't identify
                    errorCode = "unknown_format";
                    progress?.Report($"无法自动识别模组结构: {archiveName}");
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

            // Duplicate — ask user
            var overwrite = Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    $"该模组已存在: {entry.Name}\n\n是否替换并覆盖？",
                    "重复模组",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes);

            if (overwrite)
                _storage.AddOrReplaceMod(entry);
        }

        progress?.Report(results.Count > 0 
            ? $"导入完成: {results.Count} 个模组"
            : $"导入失败: {archiveName} - 无法识别");
        
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

    private static List<DirectoryInfo> ScanModDlcRoots(string extractDir)
    {
        var allDirs = new DirectoryInfo(extractDir).GetDirectories("*", SearchOption.AllDirectories);

        var candidates = allDirs.Where(d =>
            (d.Name.StartsWith("mod", StringComparison.OrdinalIgnoreCase) ||
             d.Name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase)) &&
            d.GetDirectories("content", SearchOption.TopDirectoryOnly).Length > 0
        ).ToList();

        // Deduplicate: if modX and modX/sub/modX both found, keep deepest
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            for (int j = candidates.Count - 1; j >= 0; j--)
            {
                if (i != j && candidates[i].FullName.StartsWith(
                    candidates[j].FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.RemoveAt(j);
                    i = candidates.Count - 1;
                    break;
                }
            }
        }

        return candidates
            .OrderBy(d => d.Name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(d => d.Name)
            .ToList();
    }

    private async Task<ModEntry?> BuildModEntry(string modRoot, ModType modType, string groupId,
        string archivePath, string archiveName, IProgress<string>? progress)
    {
        progress?.Report($"正在分析: {Path.GetFileName(modRoot)}...");

        var originalName = Path.GetFileName(modRoot);
        var gameFolderName = EnsureModPrefix(originalName, modType);

        var finalModDir = Path.Combine(_storage.GetModsDirectory(), gameFolderName);
        if (Directory.Exists(finalModDir))
            finalModDir = Path.Combine(_storage.GetModsDirectory(), 
                gameFolderName + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));

        await Task.Run(() => MoveDirectory(modRoot, finalModDir));

        var modInfo = ParseModInfo(finalModDir);
        var category = DetectCategory(finalModDir, archiveName, modInfo);
        var screenshot = FindScreenshot(finalModDir, modInfo);

        return new ModEntry
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
            GameModFolderName = gameFolderName,
            GroupId = groupId,
            ImportDate = DateTime.Now,
            IsEnabled = false,
            ModType = modType
        };
    }

    private static ModType? AskModOrDlc(string archiveName)
    {
        var result = MessageBox.Show(
            $"无法自动识别 \"{archiveName}\" 的安装位置。\n\n" +
            "该模组应该安装到哪个目录？\n\n" +
            "是 = Mods 文件夹\n" +
            "否 = DLC 文件夹\n" +
            "取消 = 跳过该模组",
            "选择安装位置",
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
        // 1. Use user-configured path from settings
        var configured = _settings.Current.SevenZipPath;
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            return configured;

        // 2. Search system PATH
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

        // 3. Fallback: hardcoded candidates
        foreach (var candidate in SevenZipCandidates)
            if (File.Exists(candidate)) return candidate;

        // 4. Nothing found
        return "";
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
                    if (text.Length > 2000) text = text[..2000] + "\n\n... (内容过长已截断)";
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
                if (text.Length > 2000) text = text[..2000] + "\n\n... (内容过长已截断)";
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
}
