using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Views;

public partial class ConflictDialog : Window
{
    private readonly List<ConflictInfo> _conflicts;
    private string? _scriptMergerPath;

    public ConflictDialog(List<ConflictInfo> conflicts, 
        Dictionary<string, DateTime>? modDates = null)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _conflicts = conflicts;
        LoadConflicts(conflicts, modDates);
        AutoDetectScriptMerger();
    }

    private void AutoDetectScriptMerger()
    {
        // Try common paths
        string[] commonPaths = {
            @"D:\Tools\WitcherScriptMerger\WitcherScriptMerger.exe",
            @"C:\Program Files\WitcherScriptMerger\WitcherScriptMerger.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "WitcherScriptMerger", "WitcherScriptMerger.exe")
        };

        foreach (var p in commonPaths)
        {
            if (File.Exists(p)) { _scriptMergerPath = p; return; }
        }

        // Check settings
        try
        {
            var settingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Witcher3ModManager");
            var settingsPath = Path.Combine(settingsDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
                var mergerPath = settings?.GetProperty("ScriptMergerPath")?.GetString();
                if (!string.IsNullOrEmpty(mergerPath) && File.Exists(mergerPath))
                    _scriptMergerPath = mergerPath;
            }
        }
        catch { }
    }

    private void LoadConflicts(List<ConflictInfo> conflicts, 
        Dictionary<string, DateTime>? modDates)
    {
        if (conflicts.Count == 0)
        {
            SummaryText.Text = "未检测到模组冲突。";
            NoConflictsPanel.Visibility = Visibility.Visible;
            ConflictStackPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NoConflictsPanel.Visibility = Visibility.Collapsed;
        ConflictStackPanel.Visibility = Visibility.Visible;

        var scriptCount = conflicts.Count(c => c.IsScriptConflict);
        SummaryText.Text = $"检测到  {conflicts.Count} 个文件冲突，" +
                          $"涉及 {conflicts.SelectMany(c => c.ConflictingMods).Distinct().Count()} 个模组。";

        if (scriptCount > 0)
        {
            ScriptWarning.Visibility = Visibility.Visible;
            ScriptWarning.Text = $"⚠ 其中  {scriptCount} 个为脚本冲突，建议使用脚本合并工具处理";
            BtnScriptMerger.Visibility = Visibility.Visible;
        }

        var displayItems = conflicts.Select(c =>
        {
            var modNames = c.ConflictingMods.Select(m =>
            {
                if (modDates != null && modDates.TryGetValue(m, out var date))
                    return $"{m} (安装于 {date:yyyy-MM-dd HH:mm}）";
                return m;
            }).ToList();

            return new
            {
                ConflictSummary = string.Join(" \u2194 ", modNames),
                FilePath = c.FilePath,
                IsScript = c.IsScriptConflict
            };
        }).ToList();

        ConflictStackPanel.Children.Clear();
        foreach (var item in displayItems)
        {
            var card = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1814161E")),
                CornerRadius = new System.Windows.CornerRadius(4),
                Padding = new System.Windows.Thickness(12, 10, 12, 10),
                Margin = new System.Windows.Thickness(0, 0, 0, 6),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E2230")),
                BorderThickness = new System.Windows.Thickness(1)
            };

            var stack = new System.Windows.Controls.StackPanel();
            
            var headerRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            headerRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "\uEA39",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C9A96E")),
                Margin = new System.Windows.Thickness(0, 0, 6, 0)
            });
            headerRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = (string?)item.GetType().GetProperty("ConflictSummary")?.GetValue(item) ?? "",
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E2E4E9"))
            });
            stack.Children.Add(headerRow);

            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = (string?)item.GetType().GetProperty("FilePath")?.GetValue(item) ?? "",
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5E626E")),
                Margin = new System.Windows.Thickness(20, 3, 0, 0),
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
            });

            card.Child = stack;
            ConflictStackPanel.Children.Add(card);
        }
    }

    private void OpenScriptMerger_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_scriptMergerPath) && File.Exists(_scriptMergerPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _scriptMergerPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法启动脚本合并工具： {ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show(
                "未找到脚本合并工具。\n\n请在设置中配置脚本合并工具路径。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}


