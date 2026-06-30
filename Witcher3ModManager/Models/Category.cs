namespace Witcher3ModManager.Models;

public enum CategoryType
{
    All,
    Geralt,
    Yennefer,
    Triss,
    Graphics,
    Characters,
    ArmorWeapons,
    Gameplay,
    Quests,
    UI,
    Other
}

public class Category
{
    public CategoryType Type { get; set; }
    public string DisplayName { get; set; } = "";
    public int ModCount { get; set; }

    public static List<Category> GetDefaultCategories()
    {
        return new List<Category>
        {
            new() { Type = CategoryType.All, DisplayName = "category.all" },
            new() { Type = CategoryType.Geralt, DisplayName = "category.geralt" },
            new() { Type = CategoryType.Yennefer, DisplayName = "category.yennefer" },
            new() { Type = CategoryType.Triss, DisplayName = "category.triss" },
            new() { Type = CategoryType.Graphics, DisplayName = "category.graphics" },
            new() { Type = CategoryType.Characters, DisplayName = "category.characters" },
            new() { Type = CategoryType.ArmorWeapons, DisplayName = "category.armor_weapons" },
            new() { Type = CategoryType.Gameplay, DisplayName = "category.gameplay" },
            new() { Type = CategoryType.Quests, DisplayName = "category.quests" },
            new() { Type = CategoryType.UI, DisplayName = "category.ui" },
            new() { Type = CategoryType.Other, DisplayName = "category.other" },
        };
    }
}
