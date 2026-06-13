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
            new() { Type = CategoryType.All, DisplayName = "全部" },
            new() { Type = CategoryType.Geralt, DisplayName = "杰洛特" },
            new() { Type = CategoryType.Yennefer, DisplayName = "叶奈法" },
            new() { Type = CategoryType.Triss, DisplayName = "特莉丝" },
            new() { Type = CategoryType.Graphics, DisplayName = "图形" },
            new() { Type = CategoryType.Characters, DisplayName = "角色" },
            new() { Type = CategoryType.ArmorWeapons, DisplayName = "护甲武器" },
            new() { Type = CategoryType.Gameplay, DisplayName = "玩法" },
            new() { Type = CategoryType.Quests, DisplayName = "任务" },
            new() { Type = CategoryType.UI, DisplayName = "UI" },
            new() { Type = CategoryType.Other, DisplayName = "其他" },
        };
    }
}
