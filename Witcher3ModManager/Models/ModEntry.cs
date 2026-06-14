using System.Text.Json.Serialization;

namespace Witcher3ModManager.Models;

public enum ModType
{
    Mod,    // Installs to [GameRoot]\mods\
    Dlc     // Installs to [GameRoot]\dlc\
}

public class ModEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public CategoryType Category { get; set; } = CategoryType.Other;
    public string ScreenshotPath { get; set; } = "";
    public string CoverImagePath { get; set; } = "";
    public string SourceArchive { get; set; } = "";
    public string InstalledPath { get; set; } = "";
    public bool IsEnabled { get; set; }
    public int Priority { get; set; }
    public DateTime ImportDate { get; set; } = DateTime.Now;
    public string? GameModFolderName { get; set; }
    public string? GameDlcFolderName { get; set; }

    [JsonIgnore]
    public bool HasDlcComponent => !string.IsNullOrWhiteSpace(GameDlcFolderName);
    public string GroupId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(UserDisplayName) ? Name : UserDisplayName;
    public ModType ModType { get; set; } = ModType.Mod;

    [JsonIgnore]
    public string DisplayCategory => Category switch
    {
        CategoryType.All => "全部",
        CategoryType.Geralt => "杰洛特",
        CategoryType.Yennefer => "叶奈法",
        CategoryType.Triss => "特莉丝",
        CategoryType.Graphics => "图形",
        CategoryType.Characters => "角色",
        CategoryType.ArmorWeapons => "护甲武器",
        CategoryType.Gameplay => "玩法",
        CategoryType.Quests => "任务",
        CategoryType.UI => "UI",
        _ => "其他"
    };

    [JsonIgnore]
    public string DisplayModType => ModType == ModType.Dlc ? "DLC" : "Mod";
}