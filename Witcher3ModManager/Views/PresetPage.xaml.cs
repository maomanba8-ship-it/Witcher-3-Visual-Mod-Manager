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
    private IDisposable? _languageSubscription;

    public PresetPage()
    {
        InitializeComponent();
        RefreshLanguage();
        // Subscribe on every show, not in the constructor: this page instance is cached and
        // reused, so a constructor-only subscription would be dropped by the Unloaded handler
        // below and the page would stop following language changes after the first navigation.
        Loaded += (_, _) =>
        {
            _languageSubscription?.Dispose();
            _languageSubscription = TranslationService.Instance.Subscribe(() => Dispatcher.Invoke(RefreshLanguage));
            RefreshLanguage();
        };
        Unloaded += (_, _) =>
        {
            _languageSubscription?.Dispose();
            _languageSubscription = null;
        };
    }

    private void RefreshLanguage()
    {
        var ts = TranslationService.Instance;
        BackBtnText.Text = ts.T("settings.back");
        PageTitleText.Text = ts.T("preset.title");
        SaveBtnText.Text = ts.T("preset.save_current");
        EmptyText.Text = ts.T("preset.no_presets");
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
                Text = string.Format(TranslationService.Instance.T("preset.created_at"), preset.CreatedAt),
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5E626E")),
                Margin = new Thickness(0, 4, 0, 0)
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = string.Format(TranslationService.Instance.T("preset.mod_count"), preset.ModStates.Count),
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
                Content = TranslationService.Instance.T("preset.load_btn"),
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
                ToolTip = TranslationService.Instance.T("preset.delete_tooltip"),
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

        var result = InputDialog.Show(TranslationService.Instance.T("preset.save_title"), "\uE8EC", TranslationService.Instance.T("preset.save_prompt"), TranslationService.Instance.T("preset.save_default_name"), TranslationService.Instance.T("dialog.ok"), TranslationService.Instance.T("dialog.cancel"));
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

            var confirmed = ConfirmDialog.Show(TranslationService.Instance.T("preset.load_title"),
                string.Format(TranslationService.Instance.T("preset.load_message"), presetName),
                "\uE81E", TranslationService.Instance.T("preset.load_confirm"), TranslationService.Instance.T("dialog.cancel"),
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8BA4B5"));

            if (!confirmed) return;

            var allMods = _storage.GetAllMods().ToList();
            var troubled = new List<string>();
            foreach (var mod in allMods)
            {
                bool shouldEnable = false; foreach (var f in mod.GameModFolders) { if (preset.ModStates.TryGetValue(f, out var en) && en) { shouldEnable = true; break; } }
                
                if (shouldEnable && !mod.IsEnabled)
                {
                    if (_w3.GetGamePath() != null)
                    {
                        var outcome = _w3.EnableMod(mod);
                        if (outcome.Ok) { mod.IsEnabled = true; }
                        // Either it failed, or it enabled with folders that had nothing to deploy
                        // from. Both are worth naming once, at the end.
                        if (outcome.HasNotes) troubled.Add(OperationReport.OneLine(mod.Name, outcome));
                    }
                    else { mod.IsEnabled = true; }
                    _storage.UpdateMod(mod);
                }
                else if (!shouldEnable && mod.IsEnabled)
                {
                    if (_w3.GetGamePath() != null)
                    {
                        var outcome = _w3.DisableMod(mod);
                        if (!outcome.Ok)
                        {
                            // Disable failure: do NOT flip IsEnabled; keep the record as-is.
                            troubled.Add(OperationReport.OneLine(mod.Name, outcome));
                            continue;
                        }
                        // Disabled, but something could not be put back: name it at the end too.
                        if (outcome.HasNotes) troubled.Add(OperationReport.OneLine(mod.Name, outcome));
                    }
                    mod.IsEnabled = false;
                    // keep GameModFolderName intact
                    _storage.UpdateMod(mod);
                }
            }

            PresetApplied?.Invoke();
var msg = string.Format(TranslationService.Instance.T("preset.load_ok"), presetName, allMods.Count(m => m.IsEnabled));
            if (troubled.Count > 0)
                msg += "\n\n" + string.Format(TranslationService.Instance.T("preset.load_issues"),
                    string.Join("\n", troubled));
            MessageBox.Show(msg, TranslationService.Instance.T("preset.load_done"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_presetService == null) return;
        if (sender is Button btn && btn.Tag is string presetName)
        {
            var confirmed = ConfirmDialog.Show(TranslationService.Instance.T("preset.delete_title"),
                string.Format(TranslationService.Instance.T("preset.delete_message"), presetName),
                "\uE74D", TranslationService.Instance.T("preset.delete_confirm"), TranslationService.Instance.T("dialog.cancel"));

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
