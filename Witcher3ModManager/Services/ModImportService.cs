using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Witcher3ModManager.Models;

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

    public async Task<ModEntry?> ImportArchiveAsync(string archivePath,
        IProgress<string>? progress = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found", archivePath);

        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        progress?.Report($"正在解压: {Path.GetFileName(archivePath)}...");

        // Extract to a temp directory
        var tempExtractDir = Path.Combine(_storage.GetModsDirectory(), 
            "_tmp_" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(tempExtractDir))
            Directory.Delete(tempExtractDir, true);

        bool extracted = await Task.Run(() => ExtractArchive(archivePath, tempExtractDir));
        if (!extracted)
        {
            progress?.Report("解压失败");
            return null;
        }

        // Find the actual mod root folder (the one with content/ inside)
        // Preserve its ORIGINAL name, don't rename to archive name
        var modRoot = FindModRoot(tempExtractDir);
        var originalFolderName = Path.GetFileName(modRoot);

        progress?.Report("正在分析模组信息...");

        var modType = DetectModType(modRoot, archiveName);
        var gameFolderName = EnsureModPrefix(originalFolderName, modType);

        // Store using the real mod folder name (e.g., "modNoFallDamage"), NOT the archive name
        var finalModDir = Path.Combine(_storage.GetModsDirectory(), gameFolderName);
        if (Directory.Exists(finalModDir))
            finalModDir = Path.Combine(_storage.GetModsDirectory(), 
                gameFolderName + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));

        // Move from temp to final storage
        MoveDirectory(modRoot, finalModDir);
        // Clean up temp
        try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }

        var modInfo = ParseModInfo(finalModDir);
        var category = DetectCategory(finalModDir, archiveName, modInfo);
        var screenshot = FindScreenshot(finalModDir, modInfo);

        var mod = new ModEntry
        {
            Name = modInfo.TryGetValue("name", out var n) ? n : archiveName,
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
            ImportDate = DateTime.Now,
            IsEnabled = false,
            ModType = modType
        };

        _storage.AddMod(mod);
        progress?.Report($"导入完成: {mod.Name} [{mod.DisplayModType}]");
        return mod;
    }

    /// <summary>
    /// Find the actual mod root folder by recursively stripping single-child wrapper folders.
    /// Stops when: folder has "content" subfolder, OR has multiple children, OR no children.
    /// Returns the ORIGINAL path without renaming — caller decides the final name.
    /// </summary>
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

            // Exactly one subfolder, no files → drill in
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
                    if (text.Length > 2000) text = text[..2000] + "\n\n... (内容过长，已截断)";
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
                if (text.Length > 2000) text = text[..2000] + "\n\n... (内容过长，已截断)";
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