using System.IO;
using System.Text.Json;

namespace Witcher3ModManager.Services;

public class TranslationService
{
    public static TranslationService Instance { get; } = new();
    private readonly string _dir;
    private readonly string _defaultDir;
    private Dictionary<string, string> _strings = new();
    private string _currentLang = "zh-CN";

    public string CurrentLanguage => _currentLang;
    public event Action? LanguageChanged;

    public TranslationService()
    {
        // Writable/override location: LocalAppData (user can write, even under Program Files install).
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataPath.FolderName, "Languages");
        try { Directory.CreateDirectory(_dir); } catch { }

        // Read-only bundled defaults next to exe (community can add languages).
        _defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");

        EnsureDefaultTranslations();

        // Load saved language preference from user-writable location.
        var prefFile = Path.Combine(_dir, "lang_pref.json");
        try
        {
            if (File.Exists(prefFile))
                _currentLang = File.ReadAllText(prefFile).Trim();
        }
        catch { /* use default */ }
        SetLanguage(_currentLang);
    }

    public void SetLanguage(string lang)
    {
        _currentLang = lang;
        LoadStrings();
        // Persist preference
        try { File.WriteAllText(Path.Combine(_dir, "lang_pref.json"), lang); } catch { }
        LanguageChanged?.Invoke();
    }

    public string T(string key)
    {
        return _strings.TryGetValue(key, out var val) ? val : key;
    }

    private void LoadStrings()
    {
        // Prefer user-writable override, then bundled default, then in-memory defaults.
        _strings = new();
        var userPath = Path.Combine(_dir, $"{_currentLang}.json");
        if (File.Exists(userPath))
        {
            try { _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(userPath)) ?? new(); }
            catch { _strings = new(); }
        }
        else
        {
            var defaultPath = Path.Combine(_defaultDir, $"{_currentLang}.json");
            if (File.Exists(defaultPath))
            {
                try { _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(defaultPath)) ?? new(); }
                catch { _strings = new(); }
            }
        }

        // Merge in-memory defaults to pick up newly added keys.
        var defaults = GetDefaultStrings(_currentLang);
        foreach (var kvp in defaults)
        {
            if (!_strings.ContainsKey(kvp.Key))
                _strings[kvp.Key] = kvp.Value;
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
                try
                {
                    _strings = GetDefaultStrings(lang);
                    var json = JsonSerializer.Serialize(_strings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                }
                catch { /* user dir not writable; fall back to in-memory defaults */ }
            }
        }
    }

    private static Dictionary<string, string> GetDefaultStrings(string lang)
    {
        return lang switch
        {
            "zh-CN" => new Dictionary<string, string>
            {
                ["app.title"] = "巫师3 Mod 管理器 v0.9.4",
                                ["home.welcome_title"] = "巫师3 Mod 管理器 v0.9.4",
                ["home.welcome_subtitle"] = "选择一个功能开始",
                ["mods.context_set_cover"] = "设置预览图片",
                ["mods.context_clear_cover"] = "清除封面",
                ["mods.no_description"] = "（无描述）",
                ["home.welcome"] = "选择一个功能开始",
                ["home.mods"] = "模组",
                ["home.conflicts"] = "冲突",
                ["home.import"] = "导入",
                ["home.launch"] = "启动",
                ["home.presets"] = "预设",
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
                ["mods.no_mods"] = "暂无模组",
                ["mods.select_mod"] = "选择一个模组",
                ["mods.storage_lost"] = "⚠ 存储目录已丢失！\n",
                ["mods.backup_all"] = "备份",
                ["mods.uninstall_all"] = "全部卸载",
                ["mods.detail_header"] = "详情",
                ["mods.cover_tooltip"] = "自定义预览图片",
                ["mods.set_cover"] = "设置封面",
                ["mods.categories"] = "分类",
                ["mods.drop_hint"] = "拖拽 .rar / .zip / .7z 文件到此处",

                ["home.mods"] = "模组",
                ["home.conflict"] = "冲突",
                ["home.import"] = "导入",
                ["home.tooltip_mods"] = "模组管理",
                ["home.tooltip_conflict"] = "冲突检测",
                ["home.tooltip_import"] = "导入模组",
                ["home.tooltip_presets"] = "预设配置",
                ["home.tooltip_launch"] = "启动游戏",
                ["mods.no_mods"] = "暂无模组",
                ["mods.select_mod"] = "选择一个模组",

                ["mods.conflict_check"] = "冲突检测",
                ["mods.author"] = "作者",
                ["mods.shared_by"] = "🔗 共享资源",
                ["mods.shared_by_who"] = "被 {0} 引用",
                ["mods.shared_uses"] = "🔗 使用共享资源: {0}",
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
                ["settings.script_merger"] = "Script Merger 路径",
                ["settings.mod_storage"] = "模组存储路径",
                ["settings.not_set"] = "（未设置）",
                ["settings.dlg_bg"] = "选择背景图片",
                ["settings.dlg_game_path"] = "选择巫师3游戏根目录（包含 content 文件夹的目录）",
                ["settings.dlg_7zip"] = "选择 7z.exe",
                ["settings.dlg_mod_storage"] = "选择模组存储文件夹",
                ["settings.dlg_backup"] = "选择备份文件夹",
                ["settings.dlg_script_merger"] = "选择 WitcherScriptMerger.exe",
                ["settings.default"] = "（使用默认背景）",
                ["settings.default_path"] = "（使用默认路径）",
                ["settings.general"] = "通用",
                ["settings.language"] = "界面语言",
                ["settings.lang_coming"] = "语言切换功能将在后续版本实现",
                ["mods.filename"] = "文件名",
                ["mods.dlc_label"] = "DLC",
                ["import.split_done"] = "导入了 {0} 个模组（组合包已拆分）",
                ["import.preview_title"] = "导入确认",
                ["import.preview_body"] = "检测到以下组件需要确认后再导入：\n{0}\n\n识别到的组件：\n{1}",
                ["import.preview_ask"] = "是否继续导入？",
                ["mods.bin_only"] = "类型：纯 Bin 组件（无 Mod/DLC 文件）\nBin",
                ["mods.files"] = "个文件",
                ["mods.files"] = "file(s)",
                ["mods.bin_label"] = "Bin",
                ["mods.install_date"] = "安装日期",
                ["mods.size"] = "大小",
                ["mods.size_unknown"] = "未知",
                ["mods.enable_failed"] = "启用失败",
                ["mods.disable_failed"] = "禁用失败",
                ["mods.delete_failed"] = "删除失败",
                ["mods.backup_done"] = "已备份 {0} 个模组。",
                ["mods.uninstall_all"] = "全部卸载",
                ["mods.uninstall_confirm"] = "卸载所有已启用模组？\n\n这将禁用并从游戏中移除它们。",
                ["mods.uninstall_done"] = "所有模组已卸载。",
                ["mods.rename_title"] = "编辑显示名",
                ["mods.rename_prompt"] = "输入自定义显示名称（不影响原始文件）：",
                ["mods.cover_select"] = "选择预览图片",
                ["mods.confirm_delete"] = "确认删除",
                ["dialog.ok"] = "确定",
                ["dialog.cancel"] = "取消",
                ["dialog.done"] = "完成",
                ["dialog.backup"] = "备份",
                ["dialog.delete"] = "删除",
                ["dialog.uninstall"] = "卸载",
                ["orphan.banner"] = "发现 {0} 个未被管理的 Mod",
                ["orphan.import_ok"] = "成功导入 {0} 个 Mod。",
                ["orphan.import_title"] = "孤儿 Mod 导入",
                ["orphan.import_all"] = "全部导入",
                ["orphan.view_details"] = "查看详情",
                ["orphan.ignore"] = "忽略",
                ["orphan.dialog.title"] = "未管理 Mod ({0})",
                ["orphan.dialog.select_all"] = "全选 / 取消全选",
                ["orphan.dialog.selected_count"] = "已选 {0} / {1}",
                ["orphan.dialog.import_selected"] = "导入选中",
                ["orphan.dialog.select_one"] = "请至少选择一个 Mod。",
                ["orphan.dialog.enabled"] = "已启用",
                ["orphan.dialog.disabled"] = "已停用",
                ["orphan.dialog.pair_badge"] = "Mod + DLC",

                ["settings.auto_backup"] = "自动备份",
                                ["settings.back"] = "返回",
                ["settings.title"] = "设置",
                ["settings.appearance"] = "外观",
                ["settings.custom_bg"] = "自定义背景图",
                ["settings.bg_hint"] = "支持 .jpg / .png 格式，推荐 1920x1080",
                ["settings.path_config"] = "路径配置",
                ["settings.game_path"] = "巫师3 游戏路径",
                ["settings.backup_path"] = "备份文件夹路径",
                ["settings.mod_storage"] = "模组存储路径",
                ["settings.game_options"] = "游戏选项",
                ["settings.launch_args"] = "启动参数",
                ["settings.launch_args_hint"] = "例如: -dx12 (DirectX 12模式) 或 --launcher-skip (跳过启动器)",
                ["settings.tools"] = "工具",
                ["settings.default_bg"] = "（使用默认背景）",
                ["settings.default_path"] = "（使用默认路径）",
                ["settings.not_set"] = "（未设置）",
                ["settings.auto_backup_desc"] = "启用模组前自动备份原始游戏文件",
                ["settings.more"] = "更多功能（即将推出）",
                ["settings.about"] = "关于",
                ["settings.about_text"] = "巫师3 Mod 管理器 v0.9.4",
                ["settings.about_tech"] = "基于 .NET 10 + WPF 构建",
                ["settings.more1"] = ". 模组优先级拖拽排序",
                ["settings.more2"] = ". 脚本合并工具集成",
                ["settings.more3"] = ". Nexus Mods 链接配置",
                ["settings.more4"] = ". 主题配色方案",
                ["settings.launch_args_tooltip"] = "例如: -dx12 --launcher-skip",
                ["status.enabled"] = "已启用 {0}",
                ["status.total"] = "总计 {0}",
                ["status.conflicts"] = "冲突 {0}",
                ["status.potential"] = "潜在覆盖 {0}",
                ["status.game_path"] = "巫师3: {0}",
                ["status.game_not_detected"] = "巫师3: 未检测",

                ["conflict.title"] = "冲突检测结果",
                ["conflict.no_conflicts"] = "未检测到模组冲突。",
                ["conflict.script_warning"] = "检测到脚本冲突，建议使用 Witcher Script Merger 工具处理",
                ["conflict.script_merger_btn"] = "脚本合并工具",
                ["category.all"] = "全部",
                ["category.geralt"] = "杰洛特",
                ["category.yennefer"] = "叶奈法",
                ["category.triss"] = "特莉丝",
                ["category.graphics"] = "图形",
                ["category.characters"] = "角色",
                ["category.armor_weapons"] = "护甲武器",
                ["category.gameplay"] = "玩法",
                ["category.quests"] = "任务",
                ["category.ui"] = "UI",
                ["category.other"] = "Other",
                ["category.other"] = "其他",
                ["conflict.close"] = "关闭",
                ["conflict.no_conflicts_title"] = "未检测到冲突",
                ["conflict.summary"] = "检测到 {0} 个文件冲突，涉及 {1} 个模组。",
                ["conflict.script_summary"] = "⚠ 其中 {0} 个为脚本冲突，建议使用脚本合并工具处理",
                ["conflict.install_date"] = "{0} (安装于 {1:yyyy-MM-dd HH:mm}）",
                ["conflict.potential_section"] = "潜在覆盖（同分类模组）",
                ["conflict.potential_hint"] = "以下模组属于同一分类，可能存在逻辑覆盖，建议检查优先级。",
                ["conflict.merger_not_found"] = "未找到脚本合并工具。\n\n请在设置中配置脚本合并工具路径。",
                ["conflict.merger_failed"] = "无法启动脚本合并工具：{0}",
                ["conflict.bundle_suggest"] = "这些 Mod 使用 Bundle 打包，内部可能包含脚本修改。如果它们同时修改了游戏玩法，建议运行 Script Merger 检查脚本冲突。纯模型、纹理和外观 Mod 一般无需处理。",
                ["conflict.bundle_detected"] = "检测到多个 Bundle 打包 Mod",
                ["conflict.bundle_note"] = "纯模型、纹理和外观 Mod 一般无需处理。",
                ["conflict.shared_title"] = "🔗 共享资源",
                ["conflict.shared_suggestion"] = "该资源被多个 Mod 共用。停用其中一个不会删除共享资源。",
                ["conflict.shared_folder_item"] = "{0} — 共用者: {1}",
                ["conflict.shared_folder_differs"] = "⚠ 这些 Mod 导入时的内容不同，磁盘上只剩一份，可能已相互覆盖。",
                ["conflict.kind_script"] = "脚本冲突",
                ["conflict.kind_override"] = "内容覆盖",
                ["conflict.kind_bin"] = "bin 覆盖风险",
                ["conflict.kind_unsupported"] = "无法解析的容器",
                ["conflict.kind_generated"] = "已知生成物差异",
                ["status.bundle"] = "Bundle: {0}",
                ["overlap.summary"] = "检测到 {0} 组潜在覆盖，涉及 {1} 个模组。\n同分类模组可能修改同一游戏内容，游戏将根据优先级(Priority)决定最终效果。",
                ["status.shared"] = "共享 {0}",
                ["overlap.cat_count"] = "{0}（{1} 个模组）",
                ["preset.load_title"] = "加载预设",
                ["preset.load_message"] = "加载预设[{0}]？\n\n当前模组状态将被替换。",
                ["preset.load_confirm"] = "确认加载",
                ["preset.load_ok"] = "预设 [{0}] 已加载。\n已启用：{1} 个模组。",
                ["preset.load_done"] = "预设已加载",
                ["preset.delete_title"] = "删除预设",
                ["preset.delete_message"] = "删除预设[{0}]？",
                ["preset.delete_confirm"] = "确认删除",
                ["preset.title"] = "预设管理",
                ["preset.save_current"] = "保存当前配置",
                ["preset.no_presets"] = "暂无预设",
                ["preset.created_at"] = "创建于 {0:yyyy-MM-dd HH:mm}",
                ["preset.mod_count"] = "包含 {0} 个模组",
                ["preset.load_btn"] = "加载",
                ["preset.delete_tooltip"] = "删除预设",
                ["preset.save_title"] = "保存预设",
                ["preset.save_prompt"] = "输入预设名称：",
                ["preset.save_default_name"] = "我的配置",
                ["settings.path_invalid"] = "路径无效",
                ["settings.game_path_set"] = "游戏根目录已设置为:\n{0}",
                ["settings.path_set"] = "路径已设置",
                ["settings.invalid_7zip"] = "所选文件不是有效的 7z.exe，请重新选择。",
                ["error.ui_init"] = "界面初始化失败: {0}",
                ["error.startup"] = "启动失败: {0}",
                ["error.ui_crash"] = "程序发生未处理的错误：\n\n{0}\n\n{1}",
                ["error.fatal"] = "严重错误",
                ["error.fatal_msg"] = "程序发生严重错误：\n\n{0}",
                ["window.minimize"] = "最小化",
                ["window.maximize"] = "最大化",
                ["window.close"] = "关闭",
                ["backup.failed"] = "备份 {0} 失败: {1}",
                ["bin.delete_warning"] = "该 Mod 包含 {0} 个 bin 文件：\n･ {1}\n\n⚠ bin 文件保留在游戏目录中，未被删除。\n如果其他 Mod 也使用这些文件，删除会导致它们失效。\n请手动检查 game/bin/ 目录。",
                ["bin.notice"] = "Bin 文件提示",
                ["potential.no_overlaps"] = "未检测到潜在覆盖",
                ["potential.title"] = "潜在覆盖",




                ["conflict.close"] = "关闭",
                ["import.failed"] = "导入失败",
                ["import.error"] = "导入出错",
                ["import.error.7z_not_found"] = "未找到 7-Zip，请安装 7-Zip 或在设置中配置 7z.exe 路径。",
                ["import.error.extract_failed"] = "解压失败，压缩包可能已损坏或格式不受支持（支持 .rar / .zip / .7z）。",
                ["import.error.bundle_patch"] = "该模组使用了 Bundle 补丁方式（quickbms），不是标准模组格式。\n请手动解压后运行其中的 Install.bat 进行安装。",
                ["import.error.unknown_format"] = "无法识别模组结构，请确认压缩包内包含 mod 或 dlc 文件夹。",
                ["import.error.unhandled"] = "未处理的错误码: {0}",

                ["import.complete"] = "导入完成",
                ["import.check_7zip"] = "请检查 7-Zip 是否已安装。",
                ["import.progress.extracting"] = "正在解压: {0}...",
                ["import.progress.extract_failed"] = "解压失败",
                ["import.progress.bundle_unsupported"] = "不支持打包模块：Bundle 补丁模块请手动运行 Install.bat",
                ["import.progress.unrecognized"] = "无法自动识别模组结构: {0}",
                ["import.progress.analyzing"] = "正在分析: {0}...",
                ["import.progress.done"] = "导入完成: {0} 个模组",
                ["import.progress.failed"] = "导入失败: {0} - 无法识别",
                ["import.duplicate_msg"] = "该模组已存在: {0}\n\n是否替换并覆盖？",
                ["import.overwrite_title"] = "检测到重复组件",
                ["import.overwrite_msg"] = "以下组件在仓库/游戏目录已存在：{0}\n\n继续将覆盖这些组件，是否继续？",
                ["import.cancelled"] = "已取消导入，未写入任何数据。",
                ["import.duplicate_title"] = "重复模组",
                ["import.ask_location_msg"] = "无法自动识别 \"{0}\" 的安装位置。\n\n该模组应该安装到哪个目录？\n\n是 = Mods 文件夹\n否 = DLC 文件夹\n取消 = 跳过该模组",
                ["import.ask_location_title"] = "选择安装位置",
                ["import.text_truncated"] = "\n\n... (内容过长已截断)",

                ["game.not_detected"] = "未检测到巫师3安装路径。请在设置中手动配置。",
                ["game.launch_failed"] = "启动游戏失败",
                ["enable.failed"] = "启用模组失败",
                ["dialog.hint"] = "提示",
                ["dialog.error"] = "错误",
                ["dialog.warning"] = "警告",
            },
            "en" => new Dictionary<string, string>
            {
                ["app.title"] = "Witcher 3 Mod Manager v0.9.4",
                                ["home.welcome_title"] = "Witcher 3 Mod Manager v0.9.4",
                ["home.welcome_subtitle"] = "Select a function to begin",
                ["mods.context_set_cover"] = "Set Preview Image",
                ["mods.context_clear_cover"] = "Clear Cover",
                ["mods.no_description"] = "(No description)",
                ["home.welcome"] = "Select a function to begin",
                ["home.mods"] = "Mods",
                ["home.conflicts"] = "Conflicts",
                ["home.import"] = "Import",
                ["home.launch"] = "Launch",
                ["home.presets"] = "Presets",
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
                ["mods.no_mods"] = "No mods installed",
                ["mods.select_mod"] = "Select a mod",
                ["mods.storage_lost"] = "⚠ Storage directory lost!\n",
                ["mods.backup_all"] = "Backup",
                ["mods.uninstall_all"] = "Uninstall All",
                ["mods.detail_header"] = "Details",
                ["mods.cover_tooltip"] = "Custom Cover Image",
                ["mods.set_cover"] = "Set Cover",
                ["mods.categories"] = "Categories",
                ["mods.drop_hint"] = "Drag .rar / .zip / .7z files here",

                ["home.mods"] = "Mods",
                ["home.conflict"] = "Conflicts",
                ["home.import"] = "Import",
                ["home.tooltip_mods"] = "Manage Mods",
                ["home.tooltip_conflict"] = "Conflict Detection",
                ["home.tooltip_import"] = "Import Mods",
                ["home.tooltip_presets"] = "Preset Configurations",
                ["home.tooltip_launch"] = "Launch Game",
                ["mods.no_mods"] = "No mods installed",
                ["mods.select_mod"] = "Select a mod",

                ["mods.conflict_check"] = "Conflict Check",
                ["mods.author"] = "Author",
                ["mods.shared_by"] = "🔗 Shared Resource",
                ["mods.shared_by_who"] = "Referenced by {0}",
                ["mods.shared_uses"] = "🔗 Uses shared resource: {0}",
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
                ["settings.script_merger"] = "Script Merger Path",
                ["settings.mod_storage"] = "Mod Storage Path",
                ["settings.not_set"] = "(Not set)",
                ["settings.dlg_bg"] = "Select Background Image",
                ["settings.dlg_game_path"] = "Select Witcher 3 root directory (the one containing 'content' folder)",
                ["settings.dlg_7zip"] = "Select 7z.exe",
                ["settings.dlg_mod_storage"] = "Select Mod Storage Folder",
                ["settings.dlg_backup"] = "Select Backup Folder",
                ["settings.dlg_script_merger"] = "Select WitcherScriptMerger.exe",
                ["settings.default"] = "(Default background)",
                ["settings.default_path"] = "(Default path)",
                ["settings.general"] = "General",
                ["settings.language"] = "Language",
                ["settings.lang_coming"] = "Language switching will be available in a future update",
                ["mods.filename"] = "Filename",
                ["mods.dlc_label"] = "DLC",
                ["import.split_done"] = "Imported {0} mod(s) (pack split)",
                ["import.preview_title"] = "Import Confirmation",
                ["import.preview_body"] = "The following components need confirmation before import:\n{0}\n\nDetected components:\n{1}",
                ["import.preview_ask"] = "Continue with the import?",
                ["mods.bin_only"] = "Type: Bin Only (no Mod/DLC)\nBin",
                ["mods.bin_label"] = "Bin",
                ["mods.install_date"] = "Install Date",
                ["mods.size"] = "Size",
                ["mods.size_unknown"] = "Unknown",
                ["mods.enable_failed"] = "Enable Failed",
                ["mods.disable_failed"] = "Disable Failed",
                ["mods.delete_failed"] = "Delete Failed",
                ["mods.backup_done"] = "Backed up {0} mod(s).",
                ["mods.uninstall_all"] = "Uninstall All",
                ["mods.uninstall_confirm"] = "Uninstall all enabled mods?\n\nThis will disable and remove them from the game.",
                ["mods.uninstall_done"] = "All mods uninstalled.",
                ["mods.rename_title"] = "Edit Display Name",
                ["mods.rename_prompt"] = "Enter custom display name (does not affect original files):",
                ["mods.cover_select"] = "Select Preview Image",
                ["mods.confirm_delete"] = "Confirm Delete",
                ["dialog.ok"] = "OK",
                ["dialog.cancel"] = "Cancel",
                ["dialog.done"] = "Done",
                ["dialog.backup"] = "Backup",
                ["dialog.delete"] = "Delete",
                ["dialog.uninstall"] = "Uninstall",
                ["orphan.banner"] = "Found {0} unmanaged mod(s)",
                ["orphan.import_ok"] = "Successfully imported {0} mod(s).",
                ["orphan.import_title"] = "Orphan Mod Import",
                ["orphan.import_all"] = "Import All",
                ["orphan.view_details"] = "View Details",
                ["orphan.ignore"] = "Ignore",
                ["orphan.dialog.title"] = "Unmanaged Mods ({0})",
                ["orphan.dialog.select_all"] = "Select All / Deselect All",
                ["orphan.dialog.selected_count"] = "Selected {0} / {1}",
                ["orphan.dialog.import_selected"] = "Import Selected",
                ["orphan.dialog.select_one"] = "Please select at least one mod.",
                ["orphan.dialog.enabled"] = "Enabled",
                ["orphan.dialog.disabled"] = "Disabled",
                ["orphan.dialog.pair_badge"] = "Mod + DLC",

                ["settings.auto_backup"] = "Auto Backup",
                                ["settings.back"] = "Back",
                ["settings.title"] = "Settings",
                ["settings.appearance"] = "Appearance",
                ["settings.custom_bg"] = "Custom Background",
                ["settings.bg_hint"] = "Supports .jpg / .png, recommended 1920x1080",
                ["settings.path_config"] = "Path Configuration",
                ["settings.game_path"] = "Witcher 3 Game Path",
                ["settings.backup_path"] = "Backup Folder Path",
                ["settings.mod_storage"] = "Mod Storage Path",
                ["settings.game_options"] = "Game Options",
                ["settings.launch_args"] = "Launch Arguments",
                ["settings.launch_args_hint"] = "e.g. -dx12 (DirectX 12 mode) or --launcher-skip",
                ["settings.tools"] = "Tools",
                ["settings.default_bg"] = "(Use default background)",
                ["settings.default_path"] = "(Use default path)",
                ["settings.not_set"] = "(Not set)",
                ["settings.auto_backup_desc"] = "Automatically backup original game files before enabling mods",
                ["settings.more"] = "More Features (Coming Soon)",
                ["settings.about"] = "About",
                ["settings.about_text"] = "Witcher 3 Mod Manager v0.9.4",
                ["settings.about_tech"] = "Built with .NET 10 + WPF",
                ["settings.more1"] = ". Mod Priority Drag & Drop Sorting",
                ["settings.more2"] = ". Script Merger Integration",
                ["settings.more3"] = ". Nexus Mods Link Configuration",
                ["settings.more4"] = ". Theme Color Schemes",
                ["settings.launch_args_tooltip"] = "e.g. -dx12 --launcher-skip",
                ["status.enabled"] = "Enabled: {0}",
                ["status.total"] = "Total: {0}",
                ["status.conflicts"] = "Conflicts: {0}",
                ["status.potential"] = "Overrides: {0}",
                ["status.game_path"] = "Witcher 3: {0}",
                ["status.game_not_detected"] = "Witcher 3: Not detected",

                ["conflict.title"] = "Conflict Detection Results",
                ["conflict.no_conflicts"] = "No mod conflicts detected.",
                ["conflict.script_warning"] = "Script conflicts detected. We recommend using Witcher Script Merger.",
                ["category.all"] = "All",
                ["conflict.script_merger_btn"] = "Script Merger",
                ["category.geralt"] = "Geralt",
                ["category.yennefer"] = "Yennefer",
                ["category.triss"] = "Triss",
                ["category.graphics"] = "Graphics",
                ["category.characters"] = "Characters",
                ["category.armor_weapons"] = "Armor & Weapons",
                ["category.gameplay"] = "Gameplay",
                ["category.quests"] = "Quests",
                ["category.ui"] = "UI",
                ["conflict.close"] = "Close",
                ["conflict.no_conflicts_title"] = "No Conflicts",
                ["conflict.summary"] = "Detected {0} file conflicts involving {1} mod(s).",
                ["conflict.script_summary"] = "⚠ {0} are script conflicts. We recommend using Script Merger.",
                ["conflict.install_date"] = "{0} (installed {1:yyyy-MM-dd HH:mm})",
                ["conflict.potential_section"] = "Potential Override (Same Category)",
                ["conflict.potential_hint"] = "These mods belong to the same category and may have logical overrides. We recommend checking priorities.",
                ["conflict.merger_not_found"] = "Script merger not found.\n\nPlease configure the path in Settings.",
                ["conflict.merger_failed"] = "Failed to launch script merger: {0}",
                ["conflict.bundle_suggest"] = "These mods use Bundle packaging and may contain internal script modifications. If they both modify gameplay, run Script Merger to check for script conflicts. Model, texture, and appearance mods generally do not require any action.",
                ["conflict.bundle_detected"] = "Multiple Bundle Mods Detected",
                ["conflict.bundle_note"] = "Model, texture, and appearance mods generally do not require any action.",
                ["conflict.shared_title"] = "🔗 Shared Resources",
                ["conflict.shared_suggestion"] = "This resource is shared by multiple mods. Disabling one will not remove the shared resource.",
                ["conflict.shared_folder_item"] = "{0} — Shared by: {1}",
                ["conflict.shared_folder_differs"] = "⚠ These mods shipped different content at import time; only one copy is left on disk, so they may have overwritten each other.",
                ["conflict.kind_script"] = "Script conflict",
                ["conflict.kind_override"] = "Content override",
                ["conflict.kind_bin"] = "bin override risk",
                ["conflict.kind_unsupported"] = "Unparseable container",
                ["conflict.kind_generated"] = "Generated artifact diff",
                ["status.bundle"] = "Bundle: {0}",
                ["status.shared"] = "Shared {0}",
                ["overlap.summary"] = "Detected {0} potential override group(s) involving {1} mod(s).\nMods in the same category may modify the same game content. The game decides priority-based.",
                ["overlap.cat_count"] = "{0} ({1} mods)",
                ["preset.load_title"] = "Load Preset",
                ["preset.load_message"] = "Load preset [{0}]?\n\nCurrent mod states will be replaced.",
                ["preset.load_confirm"] = "Confirm Load",
                ["preset.load_ok"] = "Preset [{0}] loaded.\nEnabled: {1} mod(s).",
                ["preset.load_done"] = "Preset Loaded",
                ["preset.delete_title"] = "Delete Preset",
                ["preset.delete_message"] = "Delete preset [{0}]?",
                ["preset.delete_confirm"] = "Confirm Delete",
                ["preset.title"] = "Preset Manager",
                ["preset.save_current"] = "Save Current Config",
                ["preset.no_presets"] = "No presets",
                ["preset.created_at"] = "Created {0:yyyy-MM-dd HH:mm}",
                ["preset.mod_count"] = "{0} mod(s)",
                ["preset.load_btn"] = "Load",
                ["preset.delete_tooltip"] = "Delete Preset",
                ["preset.save_title"] = "Save Preset",
                ["preset.save_prompt"] = "Enter preset name:",
                ["preset.save_default_name"] = "My Config",
                ["settings.path_invalid"] = "Invalid path",
                ["settings.game_path_invalid_msg"] = "The selected folder does not contain a 'content' folder and is not a valid Witcher 3 root directory.\n\nPlease select The Witcher 3 root directory (e.g. Steam\\steamapps\\common\\The Witcher 3).",
                ["settings.game_path_invalid_msg"] = "所选目录不包含 content 文件夹，不是有效的巫师3游戏根目录。\n\n请选择 The Witcher 3 根目录（例如：Steam\\steamapps\\common\\The Witcher 3）",
                ["settings.game_path_set"] = "Game path set to:\n{0}",
                ["settings.path_set"] = "Path Set",
                ["settings.invalid_7zip"] = "Selected file is not a valid 7z.exe. Please select again.",
                ["error.ui_init"] = "UI initialization failed: {0}",
                ["error.startup"] = "Startup failed: {0}",
                ["error.ui_crash"] = "Unhandled UI error:\n\n{0}\n\n{1}",
                ["error.fatal"] = "Fatal Error",
                ["error.fatal_msg"] = "Fatal error:\n\n{0}",
                ["window.minimize"] = "Minimize",
                ["window.maximize"] = "Maximize",
                ["window.close"] = "Close",
                ["backup.failed"] = "Failed to back up {0}: {1}",
                ["bin.delete_warning"] = "This mod contains {0} bin file(s):\n･ {1}\n\n⚠ Bin files remain in the game directory.\nIf other mods also use these files, deleting them may break those mods.\nPlease manually check the game/bin/ directory.",
                ["bin.notice"] = "Bin File Notice",
                ["potential.no_overlaps"] = "No potential overrides detected",
                ["potential.title"] = "Potential Overrides",




                ["conflict.close"] = "Close",
                ["import.failed"] = "Import Failed",
                ["import.error"] = "Import Error",
                ["import.error.7z_not_found"] = "7-Zip not found. Please install 7-Zip or configure the path in Settings.",
                ["import.error.extract_failed"] = "Extraction failed. The archive may be corrupted or the format is not supported (.rar / .zip / .7z).",
                ["import.error.bundle_patch"] = "This mod uses Bundle patching (quickbms), not a standard mod format.\nPlease extract manually and run Install.bat.",
                ["import.error.unknown_format"] = "Cannot recognize mod structure. Please ensure the archive contains a mod or dlc folder.",
                ["import.error.unhandled"] = "Unhandled error code: {0}",

                ["import.complete"] = "Import Complete",
                ["import.check_7zip"] = "Please check that 7-Zip is installed.",
                ["import.progress.extracting"] = "Extracting: {0}...",
                ["import.progress.extract_failed"] = "Extraction failed",
                ["import.progress.bundle_unsupported"] = "Bundle patching not supported: please run Install.bat manually",
                ["import.progress.unrecognized"] = "Cannot recognize mod structure: {0}",
                ["import.progress.analyzing"] = "Analyzing: {0}...",
                ["import.progress.done"] = "Import complete: {0} mod(s)",
                ["import.progress.failed"] = "Import failed: {0} - cannot identify",
                ["import.duplicate_msg"] = "Mod already exists: {0}\n\nReplace and overwrite?",
                ["import.overwrite_title"] = "Duplicate Components Detected",
                ["import.overwrite_msg"] = "These components already exist in storage/game: {0}\n\nContinuing will overwrite them. Continue?",
                ["import.cancelled"] = "Import cancelled; no data was written.",
                ["import.duplicate_title"] = "Duplicate Mod",
                ["import.ask_location_msg"] = "Cannot determine install location for \"{0}\".\n\nWhere should this mod be installed?\n\nYes = Mods folder\nNo = DLC folder\nCancel = Skip this mod",
                ["import.ask_location_title"] = "Select Install Location",
                ["import.text_truncated"] = "\n\n... (content truncated)",

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
