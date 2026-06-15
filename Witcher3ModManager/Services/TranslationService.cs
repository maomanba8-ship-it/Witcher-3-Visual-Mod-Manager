using System.IO;
using System.Text.Json;

namespace Witcher3ModManager.Services;

public class TranslationService
{
    private readonly string _dir;
    private Dictionary<string, string> _strings = new();
    private string _currentLang = "zh-CN";

    public string CurrentLanguage => _currentLang;
    public event Action? LanguageChanged;

    public TranslationService()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataPath.FolderName, "translations");
        Directory.CreateDirectory(_dir);
        EnsureDefaultTranslations();
    }

    public void SetLanguage(string lang)
    {
        _currentLang = lang;
        LoadStrings();
        LanguageChanged?.Invoke();
    }

    public string T(string key)
    {
        return _strings.TryGetValue(key, out var val) ? val : key;
    }

    private void LoadStrings()
    {
        var path = Path.Combine(_dir, $"{_currentLang}.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            catch
            {
                _strings = GetDefaultStrings(_currentLang);
            }
        }
        else
        {
            _strings = GetDefaultStrings(_currentLang);
            SaveStrings();
        }
    }

    private void SaveStrings()
    {
        var path = Path.Combine(_dir, $"{_currentLang}.json");
        var json = JsonSerializer.Serialize(_strings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void EnsureDefaultTranslations()
    {
        foreach (var lang in new[] { "zh-CN", "en" })
        {
            var path = Path.Combine(_dir, $"{lang}.json");
            if (!File.Exists(path))
            {
                _strings = GetDefaultStrings(lang);
                var json = JsonSerializer.Serialize(_strings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
        }
    }

    private static Dictionary<string, string> GetDefaultStrings(string lang)
    {
        return lang switch
        {
            "zh-CN" => new Dictionary<string, string>
            {
                ["app.title"] = "巫师3 Mod 管理器",
                ["home.welcome"] = "选择一个功能开始",
                ["home.mods"] = "模组",
                ["home.conflicts"] = "冲突",
                ["home.import"] = "导入",
                ["home.launch"] = "启动",
                ["home.settings"] = "设置",
                ["nav.back"] = "返回",
                ["mods.title"] = "模组管理",
                ["mods.categories"] = "模组分类",
                ["mods.details"] = "模组详情",
                ["mods.empty"] = "暂无模组",
                ["mods.drag_hint"] = "拖拽 .rar / .zip / .7z 文件到此处导入",
                ["mods.select_detail"] = "选择模组查看详情",
                ["mods.search"] = "搜索",
                ["mods.import"] = "导入模组",
                ["mods.conflict_check"] = "冲突检测",
                ["mods.author"] = "作者",
                ["mods.version"] = "版本",
                ["mods.category"] = "分类",
                ["mods.no_description"] = "暂无描述",
                ["mods.unknown_author"] = "未知",
                ["status.enabled"] = "已启用",
                ["status.total"] = "总计",
                ["status.conflicts"] = "冲突",
                ["status.no_game"] = "巫师3: 未检测",
                ["settings.title"] = "设置",
                ["settings.appearance"] = "外观",
                ["settings.bg_image"] = "自定义背景图",
                ["settings.bg_browse"] = "浏览",
                ["settings.bg_reset"] = "重置默认背景",
                ["settings.bg_hint"] = "支持 .jpg / .png 格式，推荐 1920x1080",
                ["settings.paths"] = "路径配置",
                ["settings.game_path"] = "巫师3 游戏路径",
                ["settings.zip_path"] = "7-Zip 路径",
                ["settings.mod_storage"] = "模组存储路径",
                ["settings.not_set"] = "（未设置）",
                ["settings.default"] = "（使用默认背景）",
                ["settings.default_path"] = "（使用默认路径）",
                ["settings.general"] = "通用",
                ["settings.language"] = "界面语言",
                ["settings.lang_coming"] = "语言切换功能将在后续版本实现",
                ["settings.auto_backup"] = "自动备份",
                ["settings.auto_backup_desc"] = "启用模组前自动备份原始游戏文件",
                ["settings.more"] = "更多功能（即将推出）",
                ["settings.about"] = "关于",
                ["settings.about_text"] = "巫师3 Mod 管理器 v1.0.0-dev",
                ["settings.about_tech"] = "基于 .NET 10 + WPF 构建",
                ["conflict.title"] = "冲突检测结果",
                ["conflict.no_conflicts"] = "未检测到模组冲突。",
                ["conflict.script_warning"] = "检测到脚本冲突，建议使用 Witcher Script Merger 工具处理",
                ["conflict.close"] = "关闭",
                ["import.failed"] = "导入失败",
                ["import.error"] = "导入出错",
                ["import.complete"] = "导入完成",
                ["import.check_7zip"] = "请检查 7-Zip 是否已安装。",
                ["game.not_detected"] = "未检测到巫师3安装路径。请在设置中手动配置。",
                ["game.launch_failed"] = "启动游戏失败",
                ["enable.failed"] = "启用模组失败",
                ["dialog.hint"] = "提示",
                ["dialog.error"] = "错误",
                ["dialog.warning"] = "警告",
            },
            "en" => new Dictionary<string, string>
            {
                ["app.title"] = "Witcher 3 Mod Manager",
                ["home.welcome"] = "Select a function to begin",
                ["home.mods"] = "Mods",
                ["home.conflicts"] = "Conflicts",
                ["home.import"] = "Import",
                ["home.launch"] = "Launch",
                ["home.settings"] = "Settings",
                ["nav.back"] = "Back",
                ["mods.title"] = "Mod Management",
                ["mods.categories"] = "Categories",
                ["mods.details"] = "Mod Details",
                ["mods.empty"] = "No mods yet",
                ["mods.drag_hint"] = "Drag .rar / .zip / .7z files here to import",
                ["mods.select_detail"] = "Select a mod to view details",
                ["mods.search"] = "Search",
                ["mods.import"] = "Import Mod",
                ["mods.conflict_check"] = "Conflict Check",
                ["mods.author"] = "Author",
                ["mods.version"] = "Version",
                ["mods.category"] = "Category",
                ["mods.no_description"] = "No description",
                ["mods.unknown_author"] = "Unknown",
                ["status.enabled"] = "Enabled",
                ["status.total"] = "Total",
                ["status.conflicts"] = "Conflicts",
                ["status.no_game"] = "Witcher 3: Not detected",
                ["settings.title"] = "Settings",
                ["settings.appearance"] = "Appearance",
                ["settings.bg_image"] = "Custom Background",
                ["settings.bg_browse"] = "Browse",
                ["settings.bg_reset"] = "Reset to default",
                ["settings.bg_hint"] = "Supports .jpg / .png, recommended 1920x1080",
                ["settings.paths"] = "Path Configuration",
                ["settings.game_path"] = "Witcher 3 Game Path",
                ["settings.zip_path"] = "7-Zip Path",
                ["settings.mod_storage"] = "Mod Storage Path",
                ["settings.not_set"] = "(Not set)",
                ["settings.default"] = "(Default background)",
                ["settings.default_path"] = "(Default path)",
                ["settings.general"] = "General",
                ["settings.language"] = "Language",
                ["settings.lang_coming"] = "Language switching will be available in a future update",
                ["settings.auto_backup"] = "Auto Backup",
                ["settings.auto_backup_desc"] = "Automatically backup original game files before enabling mods",
                ["settings.more"] = "More Features (Coming Soon)",
                ["settings.about"] = "About",
                ["settings.about_text"] = "Witcher 3 Mod Manager v1.0.0-dev",
                ["settings.about_tech"] = "Built with .NET 10 + WPF",
                ["conflict.title"] = "Conflict Detection Results",
                ["conflict.no_conflicts"] = "No mod conflicts detected.",
                ["conflict.script_warning"] = "Script conflicts detected. We recommend using Witcher Script Merger.",
                ["conflict.close"] = "Close",
                ["import.failed"] = "Import Failed",
                ["import.error"] = "Import Error",
                ["import.complete"] = "Import Complete",
                ["import.check_7zip"] = "Please check that 7-Zip is installed.",
                ["game.not_detected"] = "Witcher 3 installation not detected. Please configure in Settings.",
                ["game.launch_failed"] = "Failed to launch game",
                ["enable.failed"] = "Failed to enable mod",
                ["dialog.hint"] = "Notice",
                ["dialog.error"] = "Error",
                ["dialog.warning"] = "Warning",
            },
            _ => new Dictionary<string, string>()
        };
    }
}
