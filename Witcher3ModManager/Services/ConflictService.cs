using System.IO;
using System.Security.Cryptography;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Manifest;
using Witcher3ModManager.Core.Shared;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class ConflictService
{
    private readonly ModStorageService _storage;
    private readonly Witcher3Service _w3;
    private List<ConflictInfo>? _cachedConflicts;
    private Dictionary<string, List<string>> _sharedBinOverlaps = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheValid;

    public ConflictService(ModStorageService storage, Witcher3Service w3)
    {
        _storage = storage;
        _w3 = w3;
#if DEBUG
        LogScanDiag($"ConflictService created. exeDir={AppContext.BaseDirectory}");
#endif
    }

    public IReadOnlyDictionary<string, List<string>> GetSharedBinOverlaps() => _sharedBinOverlaps;

    public void InvalidateCache()
    {
        _cacheValid = false;
        _cachedConflicts = null;
    }

    public List<ConflictInfo> DetectConflicts()
    {
        if (_cacheValid && _cachedConflicts != null)
            return _cachedConflicts;

        var enabledMods = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        var conflicts = BuildManifestConflicts(enabledMods);
#if DEBUG
        // Legacy game-dir scan is kept only as a DEBUG diagnostic comparison; it is no
        // longer the source of the user-facing conflict list.
        ScanGameDirectory();
#endif
        _cachedConflicts = conflicts;
        _cacheValid = true;
        return _cachedConflicts;
    }

    public List<ConflictInfo> ForceDetectConflicts()
    {
        InvalidateCache();
        return DetectConflicts();
    }

    /// <summary>
    /// Builds the user-facing conflict list from each enabled component storage manifest.
    /// This is the source of the conflict dialog and badge after the S6 switchover; the
    /// legacy game-dir scan is retained only under DEBUG for comparison.
    /// </summary>
    private List<ConflictInfo> BuildManifestConflicts(IReadOnlyList<ModEntry> enabledMods)
    {
        var storageDir = _storage?.GetModsDirectory();
        if (string.IsNullOrEmpty(storageDir) || enabledMods.Count < 2)
            return new List<ConflictInfo>();

        var claims = new List<FolderClaim>();
        foreach (var m in enabledMods)
        {
            foreach (var f in m.GameModFolders)
                if (!string.IsNullOrWhiteSpace(f)) claims.Add(new FolderClaim(m.Id, TargetDomain.Mods, f));
            foreach (var f in m.GameDlcFolders)
                if (!string.IsNullOrWhiteSpace(f)) claims.Add(new FolderClaim(m.Id, TargetDomain.Dlc, f));
        }

        var files = ManifestConflictInputProvider.Build(storageDir, claims);

        // Bin components are not folders under the mods root: each record keeps its own storage
        // root with files laid out relative to the game's bin folder. Two mods writing the same
        // bin path overwrite each other in place and disabling never undoes it, so a collision
        // here is a real override rather than an informational overlap.
        var binClaims = new List<ComponentRootClaim>();
        foreach (var m in enabledMods)
        {
            if (m.GameBinFiles.Count == 0) continue;
            if (string.IsNullOrWhiteSpace(m.BinStoragePath)) continue;
            binClaims.Add(new ComponentRootClaim(m.Id, TargetDomain.Bin, m.BinStoragePath));
        }

        if (binClaims.Count > 0)
            files = files.Concat(ManifestConflictInputProvider.BuildFromRoots(binClaims)).ToList();

        var evidence = ConflictAnalyzer.Analyze(files);
        var idToName = _storage?.GetAllMods()
                           .ToDictionary(m => m.Id, m => m.Name, StringComparer.OrdinalIgnoreCase)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Only genuine (red) conflicts are user-facing. Blue bundle/generated-artifact
        // items are NOT conflicts; bundle mods get their own blue "bundle" tag/panel.
        return AdaptEvidence(evidence, idToName)
            .Where(c => ConflictEvidenceRules.IsRealConflictKind(c.Kind))
            .ToList();
    }

    // ���� Ignore Rule System ��������������������������������������������������������������������������������
    //
    // Admission criteria (ALL three must be met):
    //   1. Present in nearly every WolvenKit-packed mod
    //   2. Overwriting it does NOT change game behavior
    //   3. It provides ZERO diagnostic value for real conflicts
    //
    // ? If a file only meets 1-2 criteria �� do NOT add it here.

    public static IReadOnlyList<IgnoredConflictRule> IgnoredConflictRules { get; } = new List<IgnoredConflictRule>
    {
        new(@"content\blob0.bundle",          "Standard WolvenKit content bundle"),
        new(@"content\buffers0.bundle",       "WolvenKit buffer bundle"),
        new(@"content\collision.cache",       "Auto-generated collision cache"),
        new(@"content\texture.cache",         "Auto-generated texture cache"),
        new(@"content\metadata.store",        "WolvenKit metadata store"),
        new(@"content\info.json",             "WolvenKit auto-generated mod metadata"),
        new(@"content\shaderDx12.cache",      "Auto-generated shader cache (DX12)"),
        new(@"content\*.w3strings",           "WolvenKit auto-generated localization stubs (per-folder, no cross-mod impact)")
    };

    /// <summary>
    /// Fast-path check: is this relative path ignorable?
    /// Uses a switch expression to avoid allocation.
    /// </summary>
    public static bool IsIgnorableConflict(string relativePath)
    {
        var normalized = relativePath.Replace('/', '\\').ToLowerInvariant();

        return normalized switch
        {
            @"content\blob0.bundle"      => true,
            @"content\buffers0.bundle"   => true,
            @"content\collision.cache"   => true,
            @"content\texture.cache"     => true,
            @"content\metadata.store"    => true,
            @"content\info.json"         => true,
            @"content\shaderdx12.cache"  => true,
            _ => normalized.EndsWith(".w3strings")
        };
    }

    /// <summary>
    /// Return the reason a path was ignored, or null if not ignored.
    /// </summary>
    public static string? GetIgnoreReason(string relativePath)
    {
        var normalized = relativePath.Replace('/', '\\');
        return IgnoredConflictRules
            .FirstOrDefault(r => string.Equals(r.Pattern, normalized, StringComparison.OrdinalIgnoreCase))
            ?.Reason;
    }

    // ���� Scanning ����������������������������������������������������������������������������������������������������

    private List<ConflictInfo> ScanGameDirectory()
    {
        var conflicts = new List<ConflictInfo>();
        var enabledMods = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
#if DEBUG
        LogScanDiag($"scan start: enabledMods={enabledMods.Count}");
#endif
        if (enabledMods.Count < 2) {  return conflicts; }

        var gamePath = _w3.GetGamePath();
#if DEBUG
        LogScanDiag($"gamePath={gamePath ?? "<null>"} exists={(!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))}");
#endif
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            return conflicts;

        var modFileMap = new Dictionary<string, List<(string Mod, string FullPath)>>(
            StringComparer.OrdinalIgnoreCase);

        // Scan mod folders (TargetDomain.Mods — separate from Dlc domain)
        var modsRoot = Path.Combine(gamePath, "mods"); var scannedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(modsRoot))
        {
            foreach (var mod in enabledMods)
            {
                foreach (var folderName in mod.GameModFolders)
                {
                    if (string.IsNullOrEmpty(folderName)) continue;
                    var modDir = Path.Combine(modsRoot, folderName);
                    if (!Directory.Exists(modDir)) continue;
                    if (!scannedDirs.Add(modDir)) continue;

                    try
                    {
                        var files = Directory.GetFiles(modDir, "*.*", SearchOption.AllDirectories);
                        
                        foreach (var file in files)
                        {
                            var relativePath = file[(modDir.Length + 1)..].Replace('\\', '/');
                            if (IsIgnorableConflict(relativePath)) continue;
                            if (!modFileMap.ContainsKey(relativePath))
                                modFileMap[relativePath] = new List<(string Mod, string FullPath)>();
                            if (!modFileMap[relativePath].Any(x => x.Mod == mod.Name))
                                modFileMap[relativePath].Add((mod.Name, file));
                        }
                    }
                    catch { }
                }
            }
        }

        // Scan DLC folders (TargetDomain.Dlc — separate from Mods domain)
        var dlcFileMap = new Dictionary<string, List<(string Mod, string FullPath)>>(
            StringComparer.OrdinalIgnoreCase);
        var dlcRoot = Path.Combine(gamePath, "dlc");
        if (Directory.Exists(dlcRoot))
        {
            foreach (var mod in enabledMods)
            {
                foreach (var dlcFolderName in mod.GameDlcFolders)
                {
                    if (string.IsNullOrEmpty(dlcFolderName)) continue;
                    var dlcDir = Path.Combine(dlcRoot, dlcFolderName);
                    if (!Directory.Exists(dlcDir)) continue;
                    if (!scannedDirs.Add(dlcDir)) continue;

                    try
                    {
                        var dlcFiles = Directory.GetFiles(dlcDir, "*.*", SearchOption.AllDirectories);
                        foreach (var dlcFile in dlcFiles)
                        {
                            var dlcRelPath = dlcFile.Substring(dlcDir.Length + 1).Replace((char)92, (char)47);
                            if (IsIgnorableConflict(dlcRelPath)) continue;
                            if (!dlcFileMap.ContainsKey(dlcRelPath))
                                dlcFileMap[dlcRelPath] = new List<(string Mod, string FullPath)>();
                            if (!dlcFileMap[dlcRelPath].Any(x => x.Mod == mod.Name))
                                dlcFileMap[dlcRelPath].Add((mod.Name, dlcFile));
                        }
                    }
                    catch { }
                }
            }
        }

        // Scan bin files — collect shared overlaps (informational, not conflicts)
        var sharedBinFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(Path.Combine(gamePath, "bin")))
        {
            var binModMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in enabledMods)
            {
                if (mod.GameBinFiles.Count == 0) continue;
                foreach (var bf in mod.GameBinFiles)
                {
                    if (binModMap.ContainsKey(bf))
                    {
                        var otherMod = binModMap[bf];
                        if (!sharedBinFiles.ContainsKey(bf))
                            sharedBinFiles[bf] = new List<string> { otherMod };
                        if (!sharedBinFiles[bf].Contains(mod.Name))
                            sharedBinFiles[bf].Add(mod.Name);
                    }
                    else
                    {
                        binModMap[bf] = mod.Name;
                    }
                }
            }
        }
        _sharedBinOverlaps = sharedBinFiles;
#if DEBUG
        LogScanDiag($"modFileMap: keys={modFileMap.Count} colliding={modFileMap.Count(k => k.Value.Count >= 2)}");
        LogScanDiag($"dlcFileMap: keys={dlcFileMap.Count} colliding={dlcFileMap.Count(k => k.Value.Count >= 2)}");
#endif
        AppendDomainConflicts(conflicts, modFileMap, TargetDomain.Mods);
        AppendDomainConflicts(conflicts, dlcFileMap, TargetDomain.Dlc);

#if DEBUG
        LogScanDiag($"scan done: reported conflicts={conflicts.Count} sharedBinOverlaps={_sharedBinOverlaps.Count}");
        LogManifestConflictComparison(conflicts, enabledMods);
#endif
        return conflicts;
    }

    // ���� Potential Override ��������������������������������������������������������������������������������

    /// <summary>
    /// Returns enabled mods that use WolvenKit bundle packaging (contain blob0.bundle).
    /// </summary>
    public List<ModEntry> GetBundleMods()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        var gamePath = _w3.GetGamePath();
        if (string.IsNullOrEmpty(gamePath)) return new();
        var bundleMods = new List<ModEntry>();
        var modsRoot = Path.Combine(gamePath, "mods");
        if (!Directory.Exists(modsRoot)) return bundleMods;
        foreach (var mod in enabled)
        {
            foreach (var folder in mod.GameModFolders)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                var bundlePath = Path.Combine(modsRoot, folder, "content", "blob0.bundle");
                if (File.Exists(bundlePath))
                {
                    bundleMods.Add(mod);
                    break;
                }
            }
        }
        return bundleMods;
    
    }
    /// <summary>
    /// Returns shared resources: folder names referenced by 2+ enabled mods
    /// that physically exist only once (shared library, not a conflict).
    /// </summary>
    public List<(string Folder, List<string> Mods)> GetSharedResources()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        var folderMods = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in enabled)
        {
            foreach (var f in mod.GameModFolders)
            {
                if (string.IsNullOrEmpty(f)) continue;
                if (!folderMods.ContainsKey(f)) folderMods[f] = new();
                if (!folderMods[f].Contains(mod.Name)) folderMods[f].Add(mod.Name);
            }
            foreach (var f in mod.GameDlcFolders)
            {
                if (string.IsNullOrEmpty(f)) continue;
                if (!folderMods.ContainsKey(f)) folderMods[f] = new();
                if (!folderMods[f].Contains(mod.Name)) folderMods[f].Add(mod.Name);
            }
        }
        return folderMods.Where(kv => kv.Value.Count >= 2)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Alias for GetSharedResources() — returns folder/mod pairs where a folder
    /// is referenced by multiple enabled mods (shared library, not a conflict).
    /// </summary>
    public List<(string Folder, List<string> Mods)> GetSharedFolderPairs()
    {
        return GetSharedResources();
    }

    /// <summary>
    /// Shared folders whose owners recorded different content when they were imported. A shared
    /// folder keeps a single copy on disk, so only the import-time record can still say whether
    /// the owners originally shipped the same bytes. Folders with incomplete history are left
    /// out entirely: unknown history is not a finding, and this never changes a conflict count.
    /// </summary>
    public List<(string Folder, List<string> Mods)> GetSharedContentDiffs()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        var differences = new List<(string Folder, List<string> Mods)>();

        foreach (var (folder, owners) in GetSharedResources())
        {
            var key = SharedResourceResolver.NormalizeFolder(folder);
            var recorded = new List<string?>();

            foreach (var owner in owners)
            {
                var mod = enabled.FirstOrDefault(m =>
                    string.Equals(m.Name, owner, StringComparison.OrdinalIgnoreCase));
                var matches = (mod?.ComponentFingerprints ?? new List<ComponentFingerprintEntry>())
                    .Where(e => string.Equals(
                        SharedResourceResolver.NormalizeFolder(e.Folder), key, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // No record, or one name claimed in two domains, is ambiguity: report nothing.
                recorded.Add(matches.Count == 1 ? matches[0].Fingerprint : null);
            }

            if (SharedFingerprintComparison.Compare(recorded) == SharedContentVerdict.Differs)
                differences.Add((folder, owners));
        }

        return differences;
    }

    public bool IsBundleMod(ModEntry mod)
    {
        if (!mod.IsEnabled) return false;
        var gamePath = _w3.GetGamePath();
        if (string.IsNullOrEmpty(gamePath)) return false;
        var modsRoot = Path.Combine(gamePath, "mods");
        foreach (var folder in mod.GameModFolders)
        {
            if (string.IsNullOrEmpty(folder)) continue;
            if (File.Exists(Path.Combine(modsRoot, folder, "content", "blob0.bundle")))
                return true;
        }
        return false;
    }

    public int DetectPotentialOverrideCount()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        return (int)enabled
            .GroupBy(m => m.Category)
            .Where(g => g.Key != CategoryType.All && g.Key != CategoryType.Other && g.Count() >= 2)
            .Sum(g => (long)g.Count() * (g.Count() - 1) / 2);
    }

    public Dictionary<CategoryType, List<string>> GetPotentialOverrides()
    {
        var enabled = _storage.GetAllMods().Where(m => m.IsEnabled).ToList();
        return enabled
            .GroupBy(m => m.Category)
            .Where(g => g.Key != CategoryType.All && g.Key != CategoryType.Other && g.Count() >= 2)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Name).ToList());
    }

    public bool HasPotentialOverride(ModEntry mod)
    {
        if (!mod.IsEnabled) return false;
        if (mod.Category == CategoryType.All || mod.Category == CategoryType.Other) return false;
        return _storage.GetAllMods()
            .Count(m => m.IsEnabled && m.Id != mod.Id && m.Category == mod.Category) >= 1;
    }

    // ���� Summaries & Queries ������������������������������������������������������������������������������

    public List<string> GetConflictSummary()
    {
        var conflicts = DetectConflicts();
        var pairMap = new Dictionary<string, (int Count, List<string> Files)>();

        foreach (var c in conflicts)
        {
            var sortedMods = c.ConflictingMods.OrderBy(x => x).ToList();
            var key = string.Join("|", sortedMods);

            if (!pairMap.ContainsKey(key))
                pairMap[key] = (0, new List<string>());

            var existing = pairMap[key];
            pairMap[key] = (existing.Count + 1,
                existing.Files.Append(c.FilePath).ToList());
        }

        return pairMap.Select(kvp =>
        {
            var mods = kvp.Key.Split('|');
            var filesStr = kvp.Value.Files.Take(3).ToList();
            var more = kvp.Value.Count > 3 ? $" (+{kvp.Value.Count - 3} more files)" : "";
            return $"{mods[0]} <-> {mods[1]}: {kvp.Value.Count} conflicting files\n  {string.Join("\n  ", filesStr)}{more}";
        }).ToList();
    }

    public List<string> CheckModConflicts(ModEntry mod)
    {
        if (!mod.IsEnabled) return new();

        var conflicts = DetectConflicts();
        return conflicts
            .Where(c => c.ConflictingMods.Contains(mod.Name) && c.ConflictingMods.Count >= 2)
            .Select(c => c.ConflictingMods.FirstOrDefault(m => m != mod.Name) ?? "?")
            .Distinct()
            .ToList();
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

#if DEBUG
    private static void LogScanDiag(string message)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "W3MM_conflict_scan.log");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }
#endif

    /// <summary>
    /// ADAPTER: turn evidence from the manifest-based analyzer into the existing UI
    /// ConflictInfo shape. Duplicate content stays suppressed; component identities are
    /// mapped to display names. This is not yet the source of the UI conflict list — it is
    /// used in DEBUG dual-mode comparison until switchover is accepted.
    /// </summary>
    private static List<ConflictInfo> AdaptEvidence(
        IReadOnlyList<ConflictEvidence> evidence,
        IReadOnlyDictionary<string, string> idToName)
    {
        var result = new List<ConflictInfo>();
        foreach (var e in evidence)
        {
            if (!ConflictEvidenceRules.IsUserFacing(e.Kind)) continue;
            var names = e.ComponentIdentities
                .Select(id => idToName.TryGetValue(id, out var name) ? name : id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Add(new ConflictInfo
            {
                FilePath = e.NormalizedRelativePath,
                ConflictingMods = names,
                IsScriptConflict = ConflictEvidenceRules.IsScript(e.Kind),
                Kind = e.Kind
            });
        }
        return result;
    }

#if DEBUG
    /// <summary>
    /// Dual-mode comparison (DEBUG only): run the manifest-based analyzer over enabled
    /// components' storage manifests and log how it differs from the legacy game-dir scan.
    /// It does NOT change what the user sees — the UI red list still comes from the legacy
    /// scan until switchover has been accepted with fixtures.
    /// </summary>
    private void LogManifestConflictComparison(List<ConflictInfo> legacy, List<ModEntry> enabledMods)
    {
        var storageDir = _storage?.GetModsDirectory();
        if (string.IsNullOrEmpty(storageDir)) return;

        var claims = new List<FolderClaim>();
        foreach (var m in enabledMods)
        {
            foreach (var f in m.GameModFolders)
                if (!string.IsNullOrWhiteSpace(f)) claims.Add(new FolderClaim(m.Id, TargetDomain.Mods, f));
            foreach (var f in m.GameDlcFolders)
                if (!string.IsNullOrWhiteSpace(f)) claims.Add(new FolderClaim(m.Id, TargetDomain.Dlc, f));
        }

        var files = ManifestConflictInputProvider.Build(storageDir, claims);
        var evidence = ConflictAnalyzer.Analyze(files);
        var idToName = _storage?.GetAllMods()
                           .ToDictionary(m => m.Id, m => m.Name, StringComparer.OrdinalIgnoreCase)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var adapted = AdaptEvidence(evidence, idToName);

        LogScanDiag($"[manifest-compare] storageDir={storageDir} files={files.Count} evidence={evidence.Count}");
        LogScanDiag($"[manifest-compare] newConflicts={adapted.Count} legacy={legacy.Count}");
        foreach (var c in adapted)
            LogScanDiag($"[manifest-compare] {c.Kind}: {c.FilePath} mods=[{string.Join(",", c.ConflictingMods)}]");
        var dupCount = evidence.Count(e => e.Kind == ConflictKind.DuplicateContent);
        LogScanDiag($"[manifest-compare] suppressedDuplicate={dupCount}");
    }
#endif
    /// <summary>
    /// Append conflicts for one domain map. Mods and Dlc are separate target domains:
    /// a file path shared between a mod folder and a dlc folder is NOT a conflict (S6).
    /// Identical same-domain content is DuplicateContent (not a conflict).
    /// </summary>
    private static void AppendDomainConflicts(
        List<ConflictInfo> conflicts,
        Dictionary<string, List<(string Mod, string FullPath)>> fileMap,
        TargetDomain domain)
    {
#if DEBUG
        var conflictsBefore = conflicts.Count;
        LogScanDiag($"[{domain}] candidate overlaps: {fileMap.Count(k => k.Value.Count >= 2)}");
        var suppressedCount = 0;
#endif
        foreach (var kvp in fileMap)
        {
            if (kvp.Value.Count >= 2)
            {
                var hashes = kvp.Value.Select(v => ComputeSha256(v.FullPath)).ToList();
                var allSame = hashes.All(h => !string.IsNullOrEmpty(h)) &&
                              hashes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                if (allSame)
                {
#if DEBUG
                    suppressedCount++;
                    LogScanDiag($"[{domain}] SUPPRESSED duplicate: {kvp.Key} mods=[{string.Join(",", kvp.Value.Select(v => v.Mod))}] hashes=[{string.Join(",", hashes)}]");
#endif
                    continue;
                }

                var isScript = kvp.Key.Contains("scripts/", StringComparison.OrdinalIgnoreCase) ||
                               kvp.Key.EndsWith(".ws", StringComparison.OrdinalIgnoreCase);

#if DEBUG
                LogScanDiag($"[{domain}] CONFLICT: {kvp.Key} mods=[{string.Join(",", kvp.Value.Select(v => v.Mod).Distinct(StringComparer.OrdinalIgnoreCase))}] script={isScript} hashes=[{string.Join(",", hashes)}]");
#endif
                conflicts.Add(new ConflictInfo
                {
                    FilePath = kvp.Key,
                    ConflictingMods = kvp.Value.Select(v => v.Mod).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Kind = ConflictAnalyzer.Classify(domain, hashes, kvp.Key),
                    IsScriptConflict = isScript
                });
            }
        }
#if DEBUG
        LogScanDiag($"[{domain}] reported conflicts: {conflicts.Count - conflictsBefore} (suppressed {suppressedCount} duplicate)");
#endif
    }
}
