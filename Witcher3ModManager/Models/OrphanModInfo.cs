namespace Witcher3ModManager.Models;

public class OrphanModInfo
{
    public string FolderName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool IsInDlcFolder { get; set; }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }

    /// <summary>
    /// If this orphan has a matching mod/dlc counterpart, that counterpart is stored here.
    /// Example: modFriendlyHUD has PairedOrphan pointing to dlcFriendlyHUD.
    /// The paired orphan is removed from the main list — it only lives here.
    /// </summary>
    public OrphanModInfo? PairedOrphan { get; set; }

    /// <summary>
    /// Whether this orphan represents a Mod+DLC pair.
    /// </summary>
    public bool HasPair => PairedOrphan != null;

    public string DisplaySize => SizeBytes switch
    {
        >= 1048576 => string.Format("{0:F1} MB", SizeBytes / 1048576.0),
        >= 1024 => string.Format("{0:F0} KB", SizeBytes / 1024.0),
        _ => SizeBytes + " B"
    };

    public string DisplayLocation => IsInDlcFolder ? "game/dlc/" : "game/mods/";

    /// <summary>
    /// Combined size of this orphan + its paired orphan.
    /// </summary>
    public long TotalSizeBytes => SizeBytes + (PairedOrphan?.SizeBytes ?? 0);

    public string DisplayTotalSize => TotalSizeBytes switch
    {
        >= 1048576 => string.Format("{0:F1} MB", TotalSizeBytes / 1048576.0),
        >= 1024 => string.Format("{0:F0} KB", TotalSizeBytes / 1024.0),
        _ => TotalSizeBytes + " B"
    };
}