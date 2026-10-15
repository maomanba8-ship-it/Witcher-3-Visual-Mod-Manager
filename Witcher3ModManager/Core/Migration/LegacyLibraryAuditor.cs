namespace Witcher3ModManager.Core.Migration;

/// <summary>
/// Read-only audit of an existing library, in the spirit of "understand before you migrate".
///
/// It answers questions the manager could not answer before: which records point at folders that
/// are not on disk, which folders are shared, which mods are enabled but absent from the game
/// directory, and which records cannot be attributed to any component at all. Nothing here writes,
/// moves or deletes: the file system is only consulted through the predicates the caller supplies,
/// so every rule is testable without a real installation.
/// </summary>
public static class LegacyLibraryAuditor
{
    public static LegacyLibraryAudit Audit(
        IEnumerable<LegacyLibraryEntry>? entries,
        Func<string, bool>? storageFolderExists = null,
        Func<string, bool, bool>? gameFolderExists = null)
    {
        var list = (entries ?? Enumerable.Empty<LegacyLibraryEntry>())
            .Where(e => e is not null)
            .ToList();
        var findings = new List<LegacyAuditFinding>();

        foreach (var entry in list)
        {
            var modFolders = Clean(entry.ModFolders);
            var dlcFolders = Clean(entry.DlcFolders);
            var binFiles = Clean(entry.BinFiles);

            if (modFolders.Count == 0 && dlcFolders.Count == 0 && binFiles.Count == 0)
            {
                findings.Add(new LegacyAuditFinding(
                    LegacyAuditKind.Unattributable, new[] { entry.Name },
                    Array.Empty<string>()));
                continue;
            }

            if (modFolders.Count == 0 && dlcFolders.Count == 0)
                findings.Add(new LegacyAuditFinding(
                    LegacyAuditKind.BinOnly, new[] { entry.Name },
                    binFiles));

            AddDuplicateTargetFindings(findings, entry.Name, modFolders, dlcFolders);

            if (storageFolderExists is not null)
                AddMissingStorageFindings(findings, entry.Name, modFolders, dlcFolders, storageFolderExists);

            if (entry.IsEnabled && gameFolderExists is not null)
                AddEnabledButMissingFindings(findings, entry.Name, modFolders, dlcFolders, gameFolderExists);
        }

        AddSharedFolderFindings(findings, list);

        return new LegacyLibraryAudit(list.Count, findings);
    }

    private static void AddDuplicateTargetFindings(
        List<LegacyAuditFinding> findings,
        string name,
        List<string> modFolders,
        List<string> dlcFolders)
    {
        foreach (var (domain, folders) in new[] { ("mods", modFolders), ("dlc", dlcFolders) })
        {
            var duplicates = folders
                .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
                findings.Add(new LegacyAuditFinding(
                    LegacyAuditKind.DuplicateTargetFolder, new[] { name },
                    duplicates));
        }
    }

    private static void AddMissingStorageFindings(
        List<LegacyAuditFinding> findings,
        string name,
        List<string> modFolders,
        List<string> dlcFolders,
        Func<string, bool> storageFolderExists)
    {
        var missing = modFolders.Concat(dlcFolders)
            .Where(f => !storageFolderExists(f))
            .ToList();

        if (missing.Count > 0)
            findings.Add(new LegacyAuditFinding(
                LegacyAuditKind.StorageFolderMissing, new[] { name },
                missing));
    }

    private static void AddEnabledButMissingFindings(
        List<LegacyAuditFinding> findings,
        string name,
        List<string> modFolders,
        List<string> dlcFolders,
        Func<string, bool, bool> gameFolderExists)
    {
        // A record is served if any of its folders is where the game expects it.
        var served = modFolders.Any(f => gameFolderExists(f, false)) ||
                     dlcFolders.Any(f => gameFolderExists(f, true));

        if (!served)
        {
            var folders = modFolders.Concat(dlcFolders).ToList();
            findings.Add(new LegacyAuditFinding(
                LegacyAuditKind.EnabledButMissingInGame, new[] { name },
                folders));
        }
    }

    private static void AddSharedFolderFindings(
        List<LegacyAuditFinding> findings,
        List<LegacyLibraryEntry> entries)
    {
        var claims = new List<(string Folder, string Name)>();
        foreach (var entry in entries)
        {
            foreach (var folder in Clean(entry.ModFolders)) claims.Add((folder, entry.Name));
            foreach (var folder in Clean(entry.DlcFolders)) claims.Add((folder, entry.Name));
        }

        var shared = claims
            .GroupBy(c => c.Folder, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in shared)
        {
            var owners = group.Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            findings.Add(new LegacyAuditFinding(
                LegacyAuditKind.SharedFolder, owners, new[] { group.Key }));
        }
    }

    private static List<string> Clean(IReadOnlyList<string>? values) =>
        (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
}
