using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using Witcher3ModManager.Models;
using Witcher3ModManager.Services;

namespace Witcher3ModManager.Views;

public partial class PresetPage : UserControl
{
    private PresetService? _presetService;
    private ModStorageService? _storage;
    private Witcher3Service? _w3;
    public event Action? NavigateBackRequested;
    public event Action? PresetApplied;

    public PresetPage()
    {
        InitializeComponent();
        BackBtnText.Text = "返回";
        PageTitleText.Text = "预设管理";
        SaveBtnText.Text = "保存当前配置";
        EmptyText.Text = "暂无预设";
    }

    public void Initialize(PresetService presetService, ModStorageService storage, Witcher3Service w3)
    {
        _presetService = presetService;
        _storage = storage;
        _w3 = w3;
        RefreshPresets();
    }

    public void RefreshPresets()
    {
        if (_presetService == null) return;
        var presets = _presetService.GetAllPresets();
        PresetStackPanel.Children.Clear();
        foreach (var preset in presets)
        {
            var card = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1F2A")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E2230")),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel();
            Grid.SetColumn(leftStack, 0);
            leftStack.Children.Add(new TextBlock
            {
                Text = preset.Name,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E2E4E9"))
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = $"创建于 {preset.CreatedAt:yyyy-MM-dd HH:mm}",
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5E626E")),
                Margin = new Thickness(0, 4, 0, 0)
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = $"包含 {preset.ModStates.Count} 个模组",
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9A9EAA")),
                Margin = new Thickness(0, 2, 0, 0)
            });
            grid.Children.Add(leftStack);

            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rightStack, 1);

            var loadBtn = new Button
            {
                Content = "加载",
                Width = 60,
                Height = 30,
                FontSize = 11,
                Tag = preset.Name,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8BA4B5")),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#14161E")),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            loadBtn.Click += LoadPreset_Click;
            loadBtn.Template = CreateButtonTemplate("#8BA4B5", "#A3B8C7");
            rightStack.Children.Add(loadBtn);

            var delBtn = new Button
            {
                Width = 30,
                Height = 30,
                Tag = preset.Name,
                Cursor = Cursors.Hand,
                ToolTip = "删除预设",
                Background = new System.Windows.Media.SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            delBtn.Click += DeletePreset_Click;
            delBtn.Content = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5E626E")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            delBtn.Template = CreateDeleteButtonTemplate();
            rightStack.Children.Add(delBtn);

            grid.Children.Add(rightStack);
            card.Child = grid;
            PresetStackPanel.Children.Add(card);
        }
        EmptyPanel.Visibility = presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_presetService == null || _storage == null) return;

        var result = InputDialog.Show("保存预设", "\uE8EC", "输入预设名称：", "我的配置", "确定", "取消");
        if (!string.IsNullOrWhiteSpace(result))
        {
            var allMods = _storage.GetAllMods().ToList();
            _presetService.SavePreset(result.Trim(), allMods);
            RefreshPresets();
        }
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_presetService == null || _storage == null || _w3 == null) return;
        if (sender is Button btn && btn.Tag is string presetName)
        {
            var preset = _presetService.GetPreset(presetName);
            if (preset == null) return;

            var confirmed = ConfirmDialog.Show("加载预设",
                $"加载预设[{presetName}]？" + "\n\n" +
                "当前模组状态将被替换。",
                "\uE81E", "确认加载", "取消",
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8BA4B5"));

            if (!confirmed) return;

            var allMods = _storage.GetAllMods().ToList();
            foreach (var mod in allMods)
            {
                bool shouldEnable = false; foreach (var f in mod.GameModFolders) { if (preset.ModStates.TryGetValue(f, out var en) && en) { shouldEnable = true; break; } }
                
                if (shouldEnable && !mod.IsEnabled)
                {
                    if (_w3.GetGamePath() != null)
                    {
                        var success = _w3.EnableMod(mod);
                        if (success) { mod.IsEnabled = true; mod.GameModFolderName = mod.Name; }
                    }
                    else { mod.IsEnabled = true; }
                    _storage.UpdateMod(mod);
                }
                else if (!shouldEnable && mod.IsEnabled)
                {
                    if (_w3.GetGamePath() != null)
                        _w3.DisableMod(mod);
                    mod.IsEnabled = false;
                    // keep GameModFolderName intact
                    _storage.UpdateMod(mod);
                }
            }

            PresetApplied?.Invoke();
            var msg = $"棰勮 [{presetName}] 已加载。";
            var enabledCount = allMods.Count(m => m.IsEnabled);
            msg += $"\n已启用：{enabledCount} 个模组。";
            MessageBox.Show(msg, "预设已加载", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_presetService == null) return;
        if (sender is Button btn && btn.Tag is string presetName)
        {
            var confirmed = ConfirmDialog.Show("删除预设",
                $"删除预设[{presetName}]？",
                "\uE74D", "确认删除", "取消");

            if (confirmed)
            {
                _presetService.DeletePreset(presetName);
                RefreshPresets();
            }
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBackRequested?.Invoke();

}


    private static ControlTemplate CreateButtonTemplate(string bgColor, string hoverColor)
    {
        var template = new ControlTemplate(typeof(Button));
        var borderElement = new FrameworkElementFactory(typeof(Border));
        borderElement.Name = "border";
        borderElement.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        borderElement.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borderElement.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
        
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderElement.AppendChild(contentPresenter);
        
        template.VisualTree = borderElement;
        return template;
    }

    private static ControlTemplate CreateDeleteButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var borderElement = new FrameworkElementFactory(typeof(Border));
        borderElement.Name = "bd";
        borderElement.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        borderElement.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borderElement.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        
        template.VisualTree = borderElement;
        return template;
}
    }
/// <summary>
