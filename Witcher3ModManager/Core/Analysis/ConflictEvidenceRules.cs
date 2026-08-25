namespace Witcher3ModManager.Core.Analysis;

/// <summary>
/// Mapping rules from evidence kinds to the user-facing conflict surface. Kept in the pure
/// layer so the adapter and any future UI stay consistent and testable.
/// </summary>
public static class ConflictEvidenceRules
{
    /// <summary>
    /// Duplicate content is a notice, not a user-facing conflict. Everything else is surfaced.
    /// </summary>
    public static bool IsUserFacing(ConflictKind kind) =>
        kind != ConflictKind.DuplicateContent;

    /// <summary>
    /// Only a script override (different hash on a .ws/.es path) is a script conflict.
    /// </summary>
    public static bool IsScript(ConflictKind kind) =>
        kind == ConflictKind.ScriptOverride;

    /// <summary>
    /// True only for kinds that are genuine, actionable conflicts (red). Informational kinds
    /// (UnsupportedContainer, GeneratedArtifactDiff) must NOT count toward the "conflicts" badge.
    /// </summary>
    public static bool IsRealConflictKind(ConflictKind kind) =>
        kind == ConflictKind.ScriptOverride ||
        kind == ConflictKind.DefiniteOverride ||
        kind == ConflictKind.BinOverrideRisk;
}