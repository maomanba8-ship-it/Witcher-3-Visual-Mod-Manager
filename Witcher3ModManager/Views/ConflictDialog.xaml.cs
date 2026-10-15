using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Witcher3ModManager.Models;
using Witcher3ModManager.Core.Analysis;
using System.Linq;
using Witcher3ModManager.Services;

namespace Witcher3ModManager.Views;

public partial class ConflictDialog : Window
{
    private readonly List<ConflictInfo> _conflicts;
    private string? _scriptMergerPath;
    private readonly List<ModEntry>? _bundleMods;
    private List<(string Folder, List<string> Mods)> _sharedFolders = new();
    private readonly HashSet<string> _sharedContentDiffs;

    public ConflictDialog(List<ConflictInfo> conflicts,
        Dictionary<string, DateTime>? modDates = null,
        Dictionary<CategoryType, List<string>>? potentialOverrides = null,
        List<ModEntry>? bundleMods = null,
        List<(string Folder, List<string> Mods)>? sharedFolders = null,
        IEnumerable<string>? sharedContentDiffs = null)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _conflicts = conflicts;
        _bundleMods = bundleMods;
        _sharedFolders = sharedFolders ?? new();
        _sharedContentDiffs = new HashSet<string>(
            sharedContentDiffs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        LoadConflicts(conflicts, modDates, potentialOverrides);
        AutoDetectScriptMerger();
        Translate();
        TranslationService.Instance.LanguageChanged += () => Dispatcher.Invoke(() => Translate());
        var t = TranslationService.Instance;
        Title = t.T("conflict.title");
        CloseBtn.Content = t.T("conflict.close");
        BtnScriptMerger.Content = t.T("conflict.script_merger_btn");
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
        Dictionary<string, DateTime>? modDates,
        Dictionary<CategoryType, List<string>>? potentialOverrides)
    {
        // Set default texts in code to avoid XAML encoding issues
        ScriptWarning.Text = TranslationService.Instance.T("conflict.script_warning");

        // Bundle mod hint
        if (_bundleMods != null && _bundleMods.Count >= 1)
        {
            BundleHintPanel.Visibility = Visibility.Visible;
            BundleTitleText.Text = TranslationService.Instance.T("conflict.bundle_detected");
            BundleModListText.Text = "● " + string.Join("\n● ", _bundleMods.Select(m => m.Name));
            BundleSuggestionText.Text = TranslationService.Instance.T("conflict.bundle_suggest");
        }


        // Shared folder hint
        if (_sharedFolders.Count > 0)
        {
            SharedHintPanel.Visibility = Visibility.Visible;
            var sharedLines = _sharedFolders.Select(sf =>
            {
                var line = string.Format(
                    TranslationService.Instance.T("conflict.shared_folder_item"), sf.Folder, string.Join(", ", sf.Mods));
                if (_sharedContentDiffs.Contains(sf.Folder))
                    line += "\n" + TranslationService.Instance.T("conflict.shared_folder_differs");
                return line;
            });
            SharedModListText.Text = string.Join("\n", sharedLines);
        }

        if (conflicts.Count == 0)
        {
            SummaryText.Text = TranslationService.Instance.T("conflict.no_conflicts");
            NoConflictsText.Text = TranslationService.Instance.T("conflict.no_conflicts_title");
            NoConflictsPanel.Visibility = Visibility.Visible;
            ConflictStackPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NoConflictsPanel.Visibility = Visibility.Collapsed;
        ConflictStackPanel.Visibility = Visibility.Visible;

        var realConflicts = conflicts.Where(c => ConflictEvidenceRules.IsRealConflictKind(c.Kind)).ToList();
        var scriptCount = realConflicts.Count(c => c.IsScriptConflict);
        SummaryText.Text = string.Format(TranslationService.Instance.T("conflict.summary"), realConflicts.Count, realConflicts.SelectMany(c => c.ConflictingMods).Distinct().Count());

        if (scriptCount > 0)
        {
            ScriptWarning.Visibility = Visibility.Visible;
            ScriptWarning.Text = string.Format(TranslationService.Instance.T("conflict.script_summary"), scriptCount);
            BtnScriptMerger.Visibility = Visibility.Visible;
        }

        var displayItems = conflicts.Select(c =>
        {
            var modNames = c.ConflictingMods.Select(m =>
            {
                if (modDates != null && modDates.TryGetValue(m, out var date))
                    return string.Format(TranslationService.Instance.T("conflict.install_date"), m, date);
                return m;
            }).ToList();

            return new
            {
                ConflictSummary = string.Join(" \u2194 ", modNames),
                FilePath = c.FilePath,
                IsScript = c.IsScriptConflict,
                Kind = c.Kind,
                KindText = c.Kind switch
                {
                    ConflictKind.ScriptOverride => TranslationService.Instance.T("conflict.kind_script"),
                    ConflictKind.UnsupportedContainer => TranslationService.Instance.T("conflict.kind_unsupported"),
                    ConflictKind.GeneratedArtifactDiff => TranslationService.Instance.T("conflict.kind_generated"),
                    _ => TranslationService.Instance.T("conflict.kind_override")
                },
                KindColor = c.Kind switch
                {
                    ConflictKind.ScriptOverride => "#D06A6A",
                    ConflictKind.UnsupportedContainer => "#5B8DB8",
                    ConflictKind.GeneratedArtifactDiff => "#5B8DB8",
                    _ => "#D06A6A"
                },
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

            if (!string.IsNullOrEmpty((string?)item.GetType().GetProperty("KindText")?.GetValue(item)))
            {
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = (string?)item.GetType().GetProperty("KindText")?.GetValue(item),
                    FontSize = 10,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                            (string?)item.GetType().GetProperty("KindColor")?.GetValue(item) ?? "#D06A6A")),
                    Margin = new System.Windows.Thickness(20, 2, 0, 0)
                });
            }
            card.Child = stack;
            ConflictStackPanel.Children.Add(card);
        }

        // Add potential overrides section
        if (potentialOverrides != null && potentialOverrides.Count > 0)
        {
            var sep = new System.Windows.Controls.Separator
            {
                Margin = new System.Windows.Thickness(0, 16, 0, 12),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2E3C"))
            };
            ConflictStackPanel.Children.Add(sep);

            var sectionTitle = new System.Windows.Controls.TextBlock
            {
                Text = TranslationService.Instance.T("conflict.potential_section"),
                FontSize = 13,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C9A96E")),
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            ConflictStackPanel.Children.Add(sectionTitle);

            var sectionHint = new System.Windows.Controls.TextBlock
            {
                Text = TranslationService.Instance.T("conflict.potential_hint"),
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5E626E")),
                Margin = new System.Windows.Thickness(0, 0, 0, 10),
                TextWrapping = System.Windows.TextWrapping.Wrap
            };
            ConflictStackPanel.Children.Add(sectionHint);

            foreach (var kvp in potentialOverrides)
            {
                var catDisplay = kvp.Key switch
                {
                    CategoryType.Geralt => TranslationService.Instance.T("category.geralt"),
                    CategoryType.Yennefer => TranslationService.Instance.T("category.yennefer"),
                    CategoryType.Triss => TranslationService.Instance.T("category.triss"),
                    CategoryType.Graphics => TranslationService.Instance.T("category.graphics"),
                    CategoryType.Characters => TranslationService.Instance.T("category.characters"),
                    CategoryType.ArmorWeapons => TranslationService.Instance.T("category.armor_weapons"),
                    CategoryType.Gameplay => TranslationService.Instance.T("category.gameplay"),
                    CategoryType.Quests => TranslationService.Instance.T("category.quests"),
                    CategoryType.UI => TranslationService.Instance.T("category.ui"),
                    _ => kvp.Key.ToString()
                };

                var ovCard = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1F2A")),
                    CornerRadius = new System.Windows.CornerRadius(4),
                    Padding = new System.Windows.Thickness(12, 10, 12, 10),
                    Margin = new System.Windows.Thickness(0, 0, 0, 6),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2E3C")),
                    BorderThickness = new System.Windows.Thickness(1)
                };

                var ovStack = new System.Windows.Controls.StackPanel();
                ovStack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = $"\uE74B {catDisplay}",
                    FontSize = 12,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C9A96E"))
                });
                ovStack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = string.Join(" \u2194 ", kvp.Value),
                    FontSize = 11,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9A9EAA")),
                    Margin = new System.Windows.Thickness(0, 4, 0, 0)
                });

                ovCard.Child = ovStack;
                ConflictStackPanel.Children.Add(ovCard);
            }
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
                    WorkingDirectory = Path.GetDirectoryName(_scriptMergerPath),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(TranslationService.Instance.T("conflict.merger_failed"), ex.Message),
                    TranslationService.Instance.T("dialog.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show(
                TranslationService.Instance.T("conflict.merger_not_found"),
                TranslationService.Instance.T("dialog.hint"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Translate()
    {
        var t = TranslationService.Instance;
        Title = t.T("conflict.title");
        CloseBtn.Content = t.T("conflict.close");
        BtnScriptMerger.Content = t.T("conflict.script_merger_btn");
        SharedTitleText.Text = t.T("conflict.shared_title");
        SharedSuggestionText.Text = t.T("conflict.shared_suggestion");
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
