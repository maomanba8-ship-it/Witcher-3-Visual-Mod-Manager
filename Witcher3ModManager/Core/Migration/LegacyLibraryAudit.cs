namespace Witcher3ModManager.Core.Migration;

/// <summary>
/// One record from the old library, reduced to the fields an audit can judge. Kept separate from
/// the model it is read from so the audit stays pure and testable without loading user data.
/// </summary>
public sealed record LegacyLibraryEntry(
    string Id,
    string Name,
    bool IsEnabled,
    IReadOnlyList<string> ModFolders,
    IReadOnlyList<string> DlcFolders,
    IReadOnlyList<string> BinFiles);

/// <summary>
/// What an audit can find. Only <see cref="SharedFolder"/> is benign; the rest are things a
/// migration would have to skip or the user would have to fix.
/// </summary>
public enum LegacyAuditKind
{
    /// <summary>The record claims no mod, dlc or bin component at all.</summary>
    Unattributable,

    /// <summary>Only bin files, no mod or dlc folder.</summary>
    BinOnly,

    /// <summary>A claimed folder is not present in the storage directory.</summary>
    StorageFolderMissing,

    /// <summary>One record lists the same target folder more than once.</summary>
    DuplicateTargetFolder,

    /// <summary>A folder is claimed by more than one record. Informational, not an error.</summary>
    SharedFolder,

    /// <summary>Enabled, but none of its folders are present in the game directory.</summary>
    EnabledButMissingInGame,

    /// <summary>A target the index says was deployed is not in the game directory any more.</summary>
    DeployedTargetMissing,

    /// <summary>A deployed target no longer holds the content this manager wrote there.</summary>
    DeployedTargetModified
}

/// <summary>
/// One finding as structured data: which records are involved, and which folders or files the
/// finding is about. Deliberately not a sentence, so the analysis layer stays language-neutral
/// and the UI can word it in the user's language.
/// </summary>
public sealed record LegacyAuditFinding(
    LegacyAuditKind Kind,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> Subjects);

/// <summary>
/// The read-only result. It describes what is there; it never changes anything, which is the
/// point: a migration may only act after the user has seen this.
/// </summary>
public sealed record LegacyLibraryAudit(int EntryCount, IReadOnlyList<LegacyAuditFinding> Findings)
{
    public IReadOnlyList<LegacyAuditFinding> OfKind(LegacyAuditKind kind) =>
        Findings.Where(f => f.Kind == kind).ToList();

    /// <summary>True when nothing needs attention except shared folders.</summary>
    public bool IsClean => Findings.All(f => f.Kind == LegacyAuditKind.SharedFolder);
}
