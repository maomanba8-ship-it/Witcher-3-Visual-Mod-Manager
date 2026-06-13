namespace Witcher3ModManager.Models;

public class ConflictInfo
{
    public string FilePath { get; set; } = "";
    public List<string> ConflictingMods { get; set; } = new();
    public bool IsScriptConflict { get; set; }

    public string ConflictSummary => $"{string.Join(" ↔ ", ConflictingMods)}";
    public int ModCount => ConflictingMods.Count;
}
