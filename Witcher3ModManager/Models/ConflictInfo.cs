using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Models;

public class ConflictInfo
{
    public string FilePath { get; set; } = "";
    public List<string> ConflictingMods { get; set; } = new();
    public bool IsScriptConflict { get; set; }
    public ConflictKind Kind { get; set; } = ConflictKind.DefiniteOverride;

    /// <summary>
    /// Name of the Script Merger output mod that covers this file. Set only when that mod is
    /// installed, enabled, and the merged file is actually present — so the state reverts to a
    /// normal conflict as soon as it is removed.
    /// </summary>
    public string? MergedBy { get; set; }

    public bool IsMergedByScriptMerger => !string.IsNullOrEmpty(MergedBy);

    public string ConflictSummary => $"{string.Join(" ↔ ", ConflictingMods)}";
    public int ModCount => ConflictingMods.Count;
}
