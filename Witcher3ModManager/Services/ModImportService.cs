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
    private readonly string _sevenZipPath;

    private static readonly string[] SevenZipCandidates =
    {
        @"D:\7-Zip\7z.exe",
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe"
    };

    public ModImportService(ModStorageService storage)
    {
        _storage = storage;
        _sevenZipPath = Find7Zip();
    }

    /// <summary>
    /// [����ծ] Import �����ع� �� BuildModEntry ֻ�������� ModEntry����ֹ�����ļ�ϵͳ��
    /// ImportArchiveAsync ͳһ���𣺽�ѹ��ɨ������������Move��SaveMod������temp��
    /// ��ǰ BuildModEntry �� ImportArchiveAsync ��ְ��߽���ģ�������£�


    /// [����ծ] ��Ӣ���л� �� ��ǰ 150+ ��Ӳ�������ģ����Ϊ Loc �ֵ� + ��̬ˢ�£���ֹ�� resx������ BAML �ػ꣩��
    /// [����ծ] bin/ Ŀ¼֧�� �� ���� Mod ��Ҫ�� bin\config\...\xxx.xml ������Ϸ bin Ŀ¼��������ģ��˵����ء�
    /// ��Ҫ�Ķ�: ModEntry.GameBinFiles, ScanBinFiles(), EnableBin/DisableBin, ConflictService bin��⡣
    /// ��ǰ״̬: bin �ļ���������Mod ���ܿ��ܲ��������޲˵����أ���
    /// ʨ�ն��ļ��� / Bathhouse��dlc / �ظ����� / Ŀ¼���� �����ⷴ�����֡�
    /// </summary>
    public async Task<List<ModEntry>> ImportArchiveAsync(string archivePath,
        IProgress<string>? progress = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found", archivePath);

        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        progress?.Report($"���ڽ�ѹ: {Path.GetFileName(archivePath)}...");

        var tempExtractDir = Path.Combine(_storage.GetModsDirectory(), 
            "_tmp_" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(tempExtractDir))
            Directory.Delete(tempExtractDir, true);

        bool extracted = await Task.Run(() => ExtractArchive(archivePath, tempExtractDir));
        if (!extracted)
        {
            progress?.Report("��ѹʧ��");
            return new List<ModEntry>();
        }

        var groupId = Guid.NewGuid().ToString("N")[..8];
        var results = new List<ModEntry>();

        // Step 0: Detect bundle-patching mods (quickbms + .bms)
        if (IsBundlePatchMod(tempExtractDir))
        {
            progress?.Report("��֧�ִ���ģ�飺Bundle ����ģ�����ֶ����� Install.bat");
            try { Directory.Delete(tempExtractDir, true); } catch { }
            return results;
        }

        // Step 1: Recursively scan for mod*/dlc* folders with content/
        var candidates = ScanModDlcRoots(tempExtractDir);

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

                    // Scan and store bin files
                    var binFiles = ScanBinFiles(tempExtractDir);
                    if (binFiles.Count > 0)
                    {
                        var binStorageDir = _storage.GetModBinDirectory(entry.Id);
                        entry.BinStoragePath = binStorageDir;
                        entry.GameBinFiles.AddRange(binFiles);
                        foreach (var bf in binFiles)
                        {
                            var src = Path.Combine(tempExtractDir, "bin", bf.Replace('/', Path.DirectorySeparatorChar));
                            var dest = Path.Combine(binStorageDir, bf.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            if (File.Exists(src)) File.Copy(src, dest, true);
                        }
                    }
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
            // Step 2: Fallback to FindModRoot
            var root = FindModRoot(tempExtractDir);
            if (root != null)
            {
                // Ask user: Mods or DLC?
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
                progress?.Report($"�޷��Զ�ʶ��ģ��ṹ: {archiveName}");
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

            // Duplicate �� ask user
            var overwrite = Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    $"��ģ���Ѵ���: {entry.Name}\n\n�Ƿ����µ��벢���ǣ�",
                    "�ظ�ģ��",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes);

            if (overwrite)
                _storage.AddOrReplaceMod(entry);
        }

        progress?.Report(results.Count > 0 
            ? $"�������: {results.Count} ��ģ��"
            : $"����ʧ��: {archiveName} - �޷�ʶ��");
        
        return results;
    }

    /// <summary>
    /// Recursively scan for mod*/dlc* folders that directly contain a "content" subfolder.
    /// Deduplicates nested matches (keeps deepest).
    /// </summary>
    private static List<string> ScanBinFiles(string extractDir)
    {
        var binDir = Path.Combine(extractDir, "bin");
        if (!Directory.Exists(binDir)) return new List<string>();

        var files = Directory.GetFiles(binDir, "*.*", SearchOption.AllDirectories);
        return files.Select(f => f.Substring(binDir.Length + 1).Replace('\\', '/')).ToList();
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
        progress?.Report($"���ڷ���: {Path.GetFileName(modRoot)}...");

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
            $"�޷��Զ�ʶ�� \"{archiveName}\" �İ�װλ�á�\n\n" +
            "���ģ��Ӧ��װ���ĸ�Ŀ¼��\n\n" +
            "�� = Mods �ļ���\n" +
            "�� = DLC �ļ���\n" +
            "ȡ�� = ������ģ��",
            "ѡ��װλ��",
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

            // Exactly one subfolder, no files �� drill in
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

            // If this folder has a "content" subfolder, it's a mod root �� stop here
            if (subDirs.Any(d => string.Equals(Path.GetFileName(d), "content", StringComparison.OrdinalIgnoreCase)))
                break;

            // If there's exactly one subfolder and no files, drill into it
            if (subDirs.Length == 1 && files.Length == 0)
            {
                current = subDirs[0];
                continue;
            }

            // Multiple items or single item with files �� this is likely the mod root
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
        foreach (var candidate in SevenZipCandidates)
            if (File.Exists(candidate)) return candidate;
        return "7z.exe";
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
                    if (text.Length > 2000) text = text[..2000] + "\n\n... (���ݹ������ѽض�)";
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
                if (text.Length > 2000) text = text[..2000] + "\n\n... (���ݹ������ѽض�)";
                return text;
            }
            catch { }
        }
        return "";
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
