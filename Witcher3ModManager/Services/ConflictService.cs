using System.IO;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Analysis.Script;
using Witcher3ModManager.Core.Deployment;
using Witcher3ModManager.Core.Manifest;
using Witcher3ModManager.Core.Shared;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

public class ConflictService
{
    private readonly ModStorageService _storage;
    private readonly Witcher3Service _w3;
    private List<ConflictInfo>? _cachedConflicts;
    private bool _cacheValid;

    public ConflictService(ModStorageService storage, Witcher3Service w3)
    {
        _storage = storage;
        _w3 = w3;
#if DEBUG
        LogScanDiag($"ConflictService created. exeDir={AppContext.BaseDirectory}");
#endif
    }

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

        var evidence = ConflictAnalyzer.Analyze(files)
            .Concat(BuildScriptSymbolConflicts(storageDir, claims))
            .ToList();
#if DEBUG
        // Diagnostics only: record the full evidence, including the informational kinds the
        // user-facing list intentionally leaves out, so a report of "no conflicts" can still be
        // told apart from "the analysis found nothing at all".
        LogScanDiag($"[manifest] storageDir={storageDir} files={files.Count} evidence={evidence.Count}");
        foreach (var e in evidence)
            LogScanDiag($"[manifest] {e.Kind}: {e.NormalizedRelativePath} components=[{string.Join(",", e.ComponentIdentities)}]");
#endif
        var idToName = _storage?.GetAllMods()
                           .ToDictionary(m => m.Id, m => m.Name, StringComparer.OrdinalIgnoreCase)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Only genuine (red) conflicts are user-facing. Blue bundle/generated-artifact
        // items are NOT conflicts; bundle mods get their own blue "bundle" tag/panel.
        return AdaptEvidence(evidence, idToName)
            .Where(c => ConflictEvidenceRules.IsRealConflictKind(c.Kind))
            .ToList();
    }

    // ���� Potential Override ��������������������������������������������������������������������������������

    /// <summary>
    /// Symbol-level script conflicts. File-level comparison cannot see these: a compiled
    /// <c>content\precompiled.rsblob</c> and a loose <c>.ws</c> sit at different paths even when
    /// they override the same class method. The storage copy is read because it is what gets
    /// deployed; a blob that cannot be parsed is logged and simply contributes no symbols.
    /// </summary>
    private static List<ConflictEvidence> BuildScriptSymbolConflicts(
        string storageDir,
        IReadOnlyList<FolderClaim> claims)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Folder)) continue;
            if (roots.ContainsKey(claim.ComponentId)) continue;

            var root = ManifestConflictInputProvider.ResolveStorageFolder(storageDir, claim.Folder, claim.Domain);
            if (root is null) continue;
            roots[claim.ComponentId] = root;
        }

        if (roots.Count < 2) return new List<ConflictEvidence>();

        var scanned = new List<ComponentSymbols>();
        foreach (var (componentId, root) in roots)
        {
            var result = ScriptSymbolScanner.ScanComponent(root);
            if (result.RsblobError is not null)
                DiagnosticLog.Write($"[scripts] {componentId}: precompiled.rsblob not readable: {result.RsblobError}");

            if (result.Symbols.Count > 0)
                scanned.Add(new ComponentSymbols(componentId, result.Symbols, result.Fingerprint));
        }

        return ScriptSymbolConflictAnalyzer.Analyze(scanned).ToList();
    }

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

    /// <summary>
    /// Disabled mods whose bin files are still present in the game directory. Disabling never
    /// removes bin files, so a disabled mod can still be affecting the game. Reported as its own
    /// fact rather than folded into the conflict list, which stays about what is enabled.
    /// </summary>
    public List<BinRetention> GetRetainedBinFiles()
    {
        var gamePath = _w3.GetGamePath();
        if (string.IsNullOrEmpty(gamePath)) return new List<BinRetention>();

        var binRoot = Path.Combine(gamePath, "bin");
        var claims = _storage.GetAllMods()
            .Where(m => m.GameBinFiles.Count > 0)
            .Select(m => new BinClaim(m.Name, m.IsEnabled, m.GameBinFiles))
            .ToList();

        return BinRetentionInspector
            .FindRetained(claims, relative =>
                File.Exists(Path.Combine(binRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
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

#if DEBUG
    private static void LogScanDiag(string message) => DiagnosticLog.Write("[conflict] " + message);
#endif

    /// <summary>
    /// ADAPTER: turn evidence from the manifest-based analyzer into the existing UI
    /// ConflictInfo shape. Duplicate content stays suppressed; component identities are
    /// mapped to display names. This is the single source of the UI conflict list.
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

}
