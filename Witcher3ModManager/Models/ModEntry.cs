using System.Text.Json.Serialization;
using Witcher3ModManager.Services;

namespace Witcher3ModManager.Models;

public enum ModType
{
    Mod,
    Dlc
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

    public List<string> GameModFolders { get; set; } = new();
    public List<string> GameDlcFolders { get; set; } = new();
    public List<string> GameBinFiles { get; set; } = new();
    public string? BinStoragePath { get; set; }

    [JsonIgnore]
    public string? GameModFolderName
    {
        get => GameModFolders.Count > 0 ? GameModFolders[0]
            : (GameDlcFolders.Count > 0 ? GameDlcFolders[0] : null);
        set { GameModFolders.Clear(); if (value != null) GameModFolders.Add(value); }
    }

    [JsonIgnore]
    public string? GameDlcFolderName
    {
        get => GameDlcFolders.Count > 0 ? GameDlcFolders[0] : null;
        set { GameDlcFolders.Clear(); if (value != null) GameDlcFolders.Add(value); }
    }

    [JsonIgnore]
    public bool HasDlcComponent => GameDlcFolders.Count > 0;
    public string GroupId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(UserDisplayName) ? Name : UserDisplayName;
    public ModType ModType { get; set; } = ModType.Mod;

    [JsonIgnore]
    public string DisplayCategory => Category switch
    {
        CategoryType.All => TranslationService.Instance.T("category.all"),
        CategoryType.Geralt => TranslationService.Instance.T("category.geralt"),
        CategoryType.Yennefer => TranslationService.Instance.T("category.yennefer"),
        CategoryType.Triss => TranslationService.Instance.T("category.triss"),
        CategoryType.Graphics => TranslationService.Instance.T("category.graphics"),
        CategoryType.Characters => TranslationService.Instance.T("category.characters"),
        CategoryType.ArmorWeapons => TranslationService.Instance.T("category.armor_weapons"),
        CategoryType.Gameplay => TranslationService.Instance.T("category.gameplay"),
        CategoryType.Quests => TranslationService.Instance.T("category.quests"),
        CategoryType.UI => TranslationService.Instance.T("category.ui"),
        _ => TranslationService.Instance.T("category.other")
    };

    [JsonIgnore]
    public string DisplayModType => ModType == ModType.Dlc ? "DLC" : "Mod";
}