using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using System.Diagnostics;
using Microsoft.Win32;
using Witcher3ModManager.Models;
using Witcher3ModManager.Services;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Views;

public partial class ModListPage : UserControl
{
    private ModStorageService? _storage;
    private Witcher3Service? _w3;
    private ConflictService? _conflict;
    private ModImportService? _importer;
    private BackupService? _backup;
    private Category? _selectedCategory;
    private List<ModEntry> _allMods = new();
    private ModEntry? _selectedMod;

    public event Action? NavigateBackRequested;
    public event Action<int, int, int, int, int, int>? StatusUpdated;  // enabled, total, conflicts, potentialOverrides, bundleCount, sharedCount
    private List<OrphanModInfo> _orphans = new();

    public ModListPage()
    {
        InitializeComponent();
    }

    public void Initialize(ModStorageService storage, ModImportService importer, 
        Witcher3Service w3, ConflictService conflict, BackupService backup)
    {
        _storage = storage;
        _importer = importer;
        _w3 = w3;
        _conflict = conflict;
        _backup = backup;
        LoadCategories();
        RefreshModList();
        RefreshLanguage();
        TranslationService.Instance.LanguageChanged += () => Dispatcher.Invoke(() => {
            RefreshLanguage();
            if (_selectedMod != null) ShowModDetail(_selectedMod);
        });
    }

    private void LoadCategories()
    {
        CategoryListBox.Items.Clear();
        var allMods = _storage?.GetAllMods() ?? new List<ModEntry>().AsReadOnly();
        foreach (var cat in Category.GetDefaultCategories())
        {
            var count = cat.Type == CategoryType.All 
                ? allMods.Count 
                : allMods.Count(m => m.Category == cat.Type);
            var display = count > 0 ? $"{TranslationService.Instance.T(cat.DisplayName)} ({count})" : TranslationService.Instance.T(cat.DisplayName);
            CategoryListBox.Items.Add(new ListBoxItem { Content = display, Tag = cat });
        }
        CategoryListBox.SelectedIndex = 0;
    }

    public void RefreshModList()
    {
        if (_storage == null) return;
        _allMods = _storage.GetAllMods().ToList();
        LoadCategories();
        ApplyCategoryFilter();
        UpdateStatusBar();
    }

    public void SetOrphans(List<OrphanModInfo> orphans)
    {
        _orphans = orphans ?? new List<OrphanModInfo>();
        if (_orphans.Count == 0)
        {
            OrphanBanner.Visibility = Visibility.Collapsed;
            return;
        }
        OrphanBannerText.Text = string.Format(TranslationService.Instance.T("orphan.banner"), _orphans.Count);
        OrphanBanner.Visibility = Visibility.Visible;
    }
    private void ApplyCategoryFilter()
    {
        var filtered = _selectedCategory == null || _selectedCategory.Type == CategoryType.All
            ? _allMods
            : _allMods.Where(m => m.Category == _selectedCategory.Type).ToList();

        // Build UI cards in code (NO BAML!)
        var sharedResources = _conflict?.GetSharedResources() ?? new List<(string Folder, List<string> Mods)>();
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var mod in filtered)
            panel.Children.Add(CreateModCard(mod, sharedResources));

        ModItemsControl.ItemsSource = null;
        ModItemsControl.Items.Clear();
        ModItemsControl.Items.Add(new ContentPresenter { Content = panel });

        WelcomePanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModListScroll.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border CreateModCard(ModEntry mod, List<(string Folder, List<string> Mods)> sharedResources)
    {
        var card = new Border
        {
            Width = 240, Height = 100, Margin = new Thickness(6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1F2A")),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2230")),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = mod
        };
        card.MouseLeftButtonDown += ModCard_Click;
        
        var grid = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: name + category + enable toggle
        var row0 = new Grid();
        row0.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row0.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            Text = mod.DisplayName, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E4E9")),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 155
        };
        var catBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#208BA4B5")),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0)
        };
        catBorder.Child = new TextBlock
        {
            Text = mod.DisplayCategory + " | " + mod.DisplayModType,
            FontSize = 10,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8BA4B5"))
        };
        leftStack.Children.Add(nameText);
        leftStack.Children.Add(catBorder);
        Grid.SetColumn(leftStack, 0);

        var chk = new CheckBox
        {
            IsChecked = mod.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Tag = mod
        };
        chk.Checked += ToggleButton_Checked;
        chk.Unchecked += ToggleButton_Unchecked;
        Grid.SetColumn(chk, 1);

        row0.Children.Add(leftStack);
        row0.Children.Add(chk);
        Grid.SetRow(row0, 0);

        // Row 1: author + delete button
        var row1 = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var authorText = new TextBlock
        {
            Text = mod.Author, FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5E626E")),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(authorText, 0);

        var delBtn = new Button
        {
            Width = 24, Height = 24, Tag = mod.Id,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5E626E")),
            Content = "\uE74D", FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12, Cursor = Cursors.Hand
        };
        delBtn.Click += DeleteMod_Click;
        
        row1.Children.Add(authorText);
        // Dots row: red (conflict) + yellow (override) + delete button
        var dotsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (_conflict != null)
        {
            var conflictingMods = _conflict.CheckModConflicts(mod);
            if (conflictingMods.Count > 0)
                dotsPanel.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 7, Height = 7,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0556A")),
                    Margin = new Thickness(0, 0, 4, 0)
                });
        }

        if (_conflict != null && _conflict.HasPotentialOverride(mod))
            dotsPanel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C9A96E")),
                Margin = new Thickness(0, 0, 4, 0)
            });

        if (_conflict != null && _conflict.IsBundleMod(mod))
            dotsPanel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8BA4B5")),
                Margin = new Thickness(0, 0, 4, 0)
            });

        // Shared resource hint: teal 🔗 on the card + tooltip listing referencers
        if (sharedResources.Count > 0)
        {
            var folders = new List<string>();
            folders.AddRange(mod.GameModFolders);
            folders.AddRange(mod.GameDlcFolders);
            var sharedOfMod = sharedResources
                .Where(sr => folders.Contains(sr.Folder, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (sharedOfMod.Count > 0)
            {
                var isPureShared = folders.Count > 0 && folders.All(f => sharedOfMod.Any(sr => sr.Folder.Equals(f, StringComparison.OrdinalIgnoreCase)));
                string hint;
                if (isPureShared)
                {
                    var others = sharedOfMod
                        .SelectMany(sr => sr.Mods)
                        .Where(n => !n.Equals(mod.Name, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var who = others.Count > 0 ? string.Join("、", others) : "?";
                    hint = TranslationService.Instance.T("mods.shared_by") + "\n"
                         + string.Format(TranslationService.Instance.T("mods.shared_by_who"), who);
                }
                else
                {
                    var foldersStr = string.Join("、", sharedOfMod.Select(sr => sr.Folder).Distinct(StringComparer.OrdinalIgnoreCase));
                    hint = string.Format(TranslationService.Instance.T("mods.shared_uses"), foldersStr);
                }
                card.ToolTip = hint;
                dotsPanel.Children.Add(new TextBlock
                {
                    Text = "🔗",
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6BA5B5")),
                    Margin = new Thickness(0, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }

        dotsPanel.Children.Add(delBtn);
        Grid.SetColumn(dotsPanel, 1);
        row1.Children.Add(dotsPanel);
        Grid.SetRow(row1, 1);

        grid.Children.Add(row0);
        grid.Children.Add(row1);


        card.Child = grid;
        return card;
    }

    private void UpdateStatusBar()
    {
        var enabled = _allMods.Count(m => m.IsEnabled);
        var total = _allMods.Count;
        var conflicts = 0;
        try { conflicts = _conflict?.DetectConflicts().Count(c => ConflictEvidenceRules.IsRealConflictKind(c.Kind)) ?? 0; } catch { }
        var overrides = _conflict?.DetectPotentialOverrideCount() ?? 0;
        var bundleCount = _conflict?.GetBundleMods().Count ?? 0;
        var sharedCount = _conflict?.GetSharedResources().Count ?? 0;
        StatusUpdated?.Invoke(enabled, total, conflicts, overrides, bundleCount, sharedCount);
    }

    #region Drag and Drop
    private void Page_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Any(IsArchive))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null)
                foreach (var f in files.Where(IsArchive))
                    await ImportFileAsync(f);
        }
    }

    private static bool IsArchive(string p) =>
        Path.GetExtension(p).ToLower() is ".rar" or ".zip" or ".7z";
    #endregion

    #region Import
    private bool _importing;
    public void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_importing) return;
        _importing = true;
        try
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            var dlg = new OpenFileDialog
            {
                Title = "Select mod archive",
                Filter = "Archive files|*.rar;*.zip;*.7z|All files|*.*",
                Multiselect = true, RestoreDirectory = true
            };
            if (dlg.ShowDialog() == true)
                foreach (var f in dlg.FileNames) _ = ImportFileAsync(f);
            dlg = null;
        }
        finally
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            _importing = false;
        }
    }

    private async Task ImportFileAsync(string filePath)
    {
        if (_importer == null || _storage == null) return;
        try
        {
            var (imported, importError) = await _importer.ImportArchiveAsync(filePath);
            if (imported.Count > 0)
            {
                _conflict?.InvalidateCache();
                RefreshModList();
                if (imported.Count > 1)
                    MessageBox.Show(string.Format(TranslationService.Instance.T("import.split_done"), imported.Count),
                        TranslationService.Instance.T("import.complete"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (importError != null)
            {
                if (importError == "cancelled")
                    return; // user intentionally cancelled; no error dialog
                var msg = importError switch
                {
                    "7z_not_found" => TranslationService.Instance.T("import.error.7z_not_found"),
                    "extract_failed" => TranslationService.Instance.T("import.error.extract_failed"),
                    "bundle_patch" => TranslationService.Instance.T("import.error.bundle_patch"),
                    "unknown_format" => TranslationService.Instance.T("import.error.unknown_format"),
                    _ => string.Format(TranslationService.Instance.T("import.error.unhandled"), importError)
                };
                MessageBox.Show(msg, TranslationService.Instance.T("import.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Import error: " + ex.Message, TranslationService.Instance.T("dialog.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    #endregion

    #region Category
    private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryListBox.SelectedItem is ListBoxItem item && item.Tag is Category cat)
        {
            _selectedCategory = cat;
            ApplyCategoryFilter();
        }
    }
    #endregion

    #region Delete
    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modId)
        {
            var mod = _allMods.FirstOrDefault(m => m.Id == modId);
            if (mod == null) return;
            var confirmed = ConfirmDialog.Show(TranslationService.Instance.T("mods.confirm_delete"),
                "Delete [" + mod.Name + "]?\n\nThis cannot be undone.",
                "\uE74D", "Delete", TranslationService.Instance.T("dialog.cancel"));
            if (!confirmed) return;
            _conflict?.InvalidateCache();
            var deleteOk = true;
            if (_w3 != null) deleteOk = _w3.DeleteMod(mod);
            if (!deleteOk)
            {
                MessageBox.Show(TranslationService.Instance.T("mods.delete_failed") + "： " + mod.Name, TranslationService.Instance.T("dialog.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _storage?.RemoveMod(modId);
            RefreshModList();
        }
    }
    #endregion

    #region Detail
    private void ModCard_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is ModEntry mod)
        {
            var menu = new ContextMenu();
            var setCoverItem = new MenuItem { Header = TranslationService.Instance.T("mods.context_set_cover") };
            setCoverItem.Click += (s, args) => SetCoverImage(mod);
            menu.Items.Add(setCoverItem);

            var clearCoverItem = new MenuItem { Header = TranslationService.Instance.T("mods.context_clear_cover") };
            clearCoverItem.Click += (s, args) => { mod.CoverImagePath = ""; _storage?.UpdateMod(mod); ShowModDetail(mod); };
            menu.Items.Add(clearCoverItem);

            menu.IsOpen = true;
        }
    }

    private void SetCoverImage(ModEntry mod)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = TranslationService.Instance.T("mods.cover_select"),
            Filter = "图片文件|*.jpg;*.jpeg;*.png|所有文件|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            var coversDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Witcher3ModManager", "Covers");
            Directory.CreateDirectory(coversDir);

            var destPath = Path.Combine(coversDir, mod.Id + Path.GetExtension(dlg.FileName));
            File.Copy(dlg.FileName, destPath, true);
            mod.CoverImagePath = destPath;
            _storage?.UpdateMod(mod);
            ShowModDetail(mod);
        }
    }

    private void ModCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is ModEntry mod)
            ShowModDetail(mod);
    }

    private void ShowModDetail(ModEntry mod)
    {
        _selectedMod = mod;
        DetailEmptyPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailName.Text = mod.DisplayName;
        DetailOriginalName.Text = mod.GameModFolders.Count > 0 ? TranslationService.Instance.T("mods.filename") + "： " + string.Join(", ", mod.GameModFolders) : "";
        DetailDlcLabel.Text = mod.GameDlcFolders.Count > 0 ? TranslationService.Instance.T("mods.dlc_label") + "： " + string.Join(", ", mod.GameDlcFolders) : "";
        DetailBinLabel.Text = mod.GameBinFiles.Count > 0
            ? (mod.GameModFolders.Count == 0 && mod.GameDlcFolders.Count == 0
                ? TranslationService.Instance.T("mods.bin_only") + "： " : TranslationService.Instance.T("mods.bin_label") + "： ")
              + mod.GameBinFiles.Count + " " + TranslationService.Instance.T("mods.files") + " ("
              + string.Join(", ", mod.GameBinFiles.Take(3))
              + (mod.GameBinFiles.Count > 3 ? "…" : "") + ")"
            : "";
        RenameBtn.Tag = mod;
        DetailAuthor.Text = TranslationService.Instance.T("mods.author") + "： " + mod.Author;
        DetailVersion.Text = TranslationService.Instance.T("mods.version") + "： " + mod.Version;
        DetailCategory.Text = TranslationService.Instance.T("mods.category") + "： " + mod.DisplayCategory + " | " + mod.DisplayModType;
        DetailPriority.Text = mod.Priority.ToString();
        DetailImportDate.Text = TranslationService.Instance.T("mods.install_date") + "： " + mod.ImportDate.ToString("yyyy-MM-dd HH:mm");
        try
        {
            if (Directory.Exists(mod.InstalledPath))
            {
                var size = new DirectoryInfo(mod.InstalledPath)
                    .GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                DetailSize.Text = TranslationService.Instance.T("mods.size") + "： " + string.Format("{0:F2} MB", size / 1024.0 / 1024.0);
            }
        }
        catch { DetailSize.Text = TranslationService.Instance.T("mods.size") + "： " + TranslationService.Instance.T("mods.size_unknown"); }
        DetailLocation.Text = mod.InstalledPath;
        DetailDescription.Text = string.IsNullOrEmpty(mod.Description) ? TranslationService.Instance.T("mods.no_description") : mod.Description;
        // Warn if storage directory is missing
        if (!string.IsNullOrEmpty(mod.InstalledPath) && !Directory.Exists(mod.InstalledPath))
            DetailDescription.Text = TranslationService.Instance.T("mods.storage_lost") + DetailDescription.Text;
        var imagePath = !string.IsNullOrEmpty(mod.CoverImagePath) && File.Exists(mod.CoverImagePath)
            ? mod.CoverImagePath : mod.ScreenshotPath;
        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            try
            {
                                var bmp = new BitmapImage();
                using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bmp.BeginInit();
                    bmp.StreamSource = fs;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelHeight = 360;
                    bmp.EndInit();
                }
                bmp.Freeze();
                DetailScreenshot.Source = bmp;

            }
            catch { DetailScreenshot.Source = null; }
        }
        else { DetailScreenshot.Source = null; }
    }
    #endregion

    #region Enable/Disable
    private void ToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk && chk.Tag is ModEntry mod)
        {
            if (_w3 != null && _w3.GetGamePath() != null)
            {
                var success = _w3.EnableMod(mod);
                if (success) { mod.IsEnabled = true; _conflict?.InvalidateCache(); }
                else
                {
                    MessageBox.Show(TranslationService.Instance.T("mods.enable_failed") + "： " + mod.Name, TranslationService.Instance.T("dialog.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    chk.IsChecked = false;
                    return;
                }
            }
            else { mod.IsEnabled = true; }
            _storage?.UpdateMod(mod);
            UpdateStatusBar();
            RefreshModList();
        }
    }

    private void ToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk && chk.Tag is ModEntry mod)
        {
            if (_w3 != null && _w3.GetGamePath() != null)
            {
                var success = _w3.DisableMod(mod);
                if (!success)
                {
                    MessageBox.Show(TranslationService.Instance.T("mods.disable_failed") + "： " + mod.Name, TranslationService.Instance.T("dialog.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    chk.IsChecked = true;
                    return;
                }
            }
            mod.IsEnabled = false;
            _conflict?.InvalidateCache();
            _storage?.UpdateMod(mod);
            UpdateStatusBar();
            RefreshModList();
        }
    }
    #endregion

    #region Priority
    private void PriorityUp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod == null || _storage == null) return;
        var sorted = _allMods.OrderBy(m => m.Priority).ToList();
        var idx = sorted.FindIndex(m => m.Id == _selectedMod.Id);
        if (idx <= 0) return;
        _storage.SwapPriorities(_selectedMod, sorted[idx - 1]);
        _storage.NormalizePriorities();
        _allMods = _allMods.OrderBy(m => m.Priority).ToList();
        ApplyCategoryFilter();
        ShowModDetail(_selectedMod);
        UpdateModsSettingsPriorities();
    }

    private void PriorityDown_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod == null || _storage == null) return;
        var sorted = _allMods.OrderBy(m => m.Priority).ToList();
        var idx = sorted.FindIndex(m => m.Id == _selectedMod.Id);
        if (idx >= sorted.Count - 1) return;
        _storage.SwapPriorities(_selectedMod, sorted[idx + 1]);
        _storage.NormalizePriorities();
        _allMods = _allMods.OrderBy(m => m.Priority).ToList();
        ApplyCategoryFilter();
        ShowModDetail(_selectedMod);
        UpdateModsSettingsPriorities();
    }

    private void UpdateModsSettingsPriorities()
    {
        if (_w3 == null || _w3.GetGamePath() == null) return;
        var enabled = _allMods.Where(m => m.IsEnabled && !string.IsNullOrEmpty(m.GameModFolderName))
            .OrderBy(m => m.Priority).Select(m => (m.GameModFolderName, m.Priority)).ToList();
        if (enabled.Count > 0) _w3.UpdateAllPriorities(enabled);
    }
    #endregion

    #region Actions
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.ToLower().Trim();
        var source = _selectedCategory == null || _selectedCategory.Type == CategoryType.All
            ? _allMods
            : _allMods.Where(m => m.Category == _selectedCategory.Type).ToList();
        
        var filtered = string.IsNullOrEmpty(query)
            ? source
            : source.Where(m => m.DisplayName.ToLower().Contains(query) ||
                               (m.Author?.ToLower().Contains(query) ?? false)).ToList();
        
        var sharedResources = _conflict?.GetSharedResources() ?? new List<(string Folder, List<string> Mods)>();
        var panel2 = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var mod in filtered) panel2.Children.Add(CreateModCard(mod, sharedResources));
        ModItemsControl.ItemsSource = null;
        ModItemsControl.Items.Clear();
        ModItemsControl.Items.Add(new ContentPresenter { Content = panel2 });
    }

    private void ConflictCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_conflict == null) return;
        var conflicts = _conflict.ForceDetectConflicts();
        var modDateMap = _allMods.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.First().ImportDate);
        var overrides = _conflict.GetPotentialOverrides();
        var sharedFolders = _conflict.GetSharedFolderPairs();
        var bundleMods = _conflict.GetBundleMods();
        var dialog = new ConflictDialog(conflicts, modDateMap, overrides, bundleMods, sharedFolders);
        dialog.ShowDialog();
        UpdateStatusBar();
    }

    private void BackupAll_Click(object sender, RoutedEventArgs e)
    {
        if (_backup == null) return;
        var results = _backup.BackupAllEnabledMods(_allMods);
        var count = results.Count(r => r.Success);
        MessageBox.Show(string.Format(TranslationService.Instance.T("mods.backup_done"), count), TranslationService.Instance.T("dialog.backup"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UninstallAll_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = ConfirmDialog.Show(TranslationService.Instance.T("mods.uninstall_all"),
            TranslationService.Instance.T("mods.uninstall_confirm"),
            "\uEA3B", TranslationService.Instance.T("dialog.uninstall"), TranslationService.Instance.T("dialog.cancel"));
        if (!confirmed) return;
        if (_backup != null) _backup.UninstallAllMods(_allMods);
        _conflict?.InvalidateCache();
        RefreshModList();
        MessageBox.Show(TranslationService.Instance.T("mods.uninstall_done"), TranslationService.Instance.T("dialog.done"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CoverBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = TranslationService.Instance.T("mods.cover_select"),
            Filter = "图片文件|*.jpg;*.jpeg;*.png|所有文件|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            var coversDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Witcher3ModManager", "Covers");
            System.IO.Directory.CreateDirectory(coversDir);

            var destPath = System.IO.Path.Combine(coversDir, _selectedMod.Id + System.IO.Path.GetExtension(dlg.FileName));
            System.IO.File.Copy(dlg.FileName, destPath, true);
            _selectedMod.CoverImagePath = destPath;
            _storage?.UpdateMod(_selectedMod);
            ShowModDetail(_selectedMod);
        }
    }

    private void RenameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ModEntry mod)
        {
            var newName = InputDialog.Show(TranslationService.Instance.T("mods.rename_title"), "\uE8EC",
                TranslationService.Instance.T("mods.rename_prompt"),
                mod.UserDisplayName ?? mod.Name,
                TranslationService.Instance.T("dialog.ok"), TranslationService.Instance.T("dialog.cancel"));
            if (newName != null)
            {
                mod.UserDisplayName = newName.Trim();
                _storage?.UpdateMod(mod);
                if (_selectedMod?.Id == mod.Id)
                    ShowModDetail(mod);
                RefreshModList();
            }
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBackRequested?.Invoke();
    }
    #endregion
    #region Orphan Handlers
    private void OrphanImportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_storage == null || _orphans.Count == 0) return;
        var dialog = new OrphanDialog(_orphans);
        if (dialog.ShowDialog() == true && dialog.SelectedOrphans.Count > 0)
        {
            var imported = 0;
            foreach (var orphan in dialog.SelectedOrphans)
            {
                try { _storage.ImportOrphanMod(orphan); imported++; }
                catch (Exception ex) { Debug.WriteLine(string.Format("Orphan import failed: {0} - {1}", orphan.FolderName, ex.Message)); }
            }
            _orphans.Clear();
            OrphanBanner.Visibility = Visibility.Collapsed;
            RefreshModList();
            MessageBox.Show(string.Format(TranslationService.Instance.T("orphan.import_ok"), imported), TranslationService.Instance.T("orphan.import_title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OrphanViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_storage == null || _orphans.Count == 0) return;
        var dialog = new OrphanDialog(_orphans);
        if (dialog.ShowDialog() == true && dialog.SelectedOrphans.Count > 0)
        {
            var imported = 0;
            foreach (var orphan in dialog.SelectedOrphans)
            {
                try { _storage.ImportOrphanMod(orphan); imported++; }
                catch (Exception ex) { Debug.WriteLine(string.Format("Orphan import failed: {0} - {1}", orphan.FolderName, ex.Message)); }
            }
            _orphans.Clear();
            OrphanBanner.Visibility = Visibility.Collapsed;
            RefreshModList();
            MessageBox.Show(string.Format(TranslationService.Instance.T("orphan.import_ok"), imported), TranslationService.Instance.T("orphan.import_title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OrphanIgnore_Click(object sender, RoutedEventArgs e)
    {
        _orphans.Clear();
        OrphanBanner.Visibility = Visibility.Collapsed;
    }
    #endregion
    private void RefreshLanguage()
    {
        var t = TranslationService.Instance;
        if (OrphanBannerText != null)
            OrphanBannerText.Text = string.Format(t.T("orphan.banner"), _orphans.Count);
        if (BtnOrphanImportAll != null)
            BtnOrphanImportAll.Content = t.T("orphan.import_all");
        if (BtnOrphanViewAll != null)
            BtnOrphanViewAll.Content = t.T("orphan.view_details");
        if (BtnOrphanIgnore != null)
            BtnOrphanIgnore.Content = t.T("orphan.ignore");
        if (PageTitleText != null) PageTitleText.Text = t.T("home.mods");
        if (BtnConflictCheck != null) BtnConflictCheck.Content = t.T("mods.conflict_check");
        if (BtnImportMod != null) BtnImportMod.Content = t.T("mods.import");
        if (EmptyModsText != null) EmptyModsText.Text = t.T("mods.no_mods");
        if (SelectModText != null) SelectModText.Text = t.T("mods.select_mod");
        if (BtnBackupAll != null) BtnBackupAll.Content = t.T("mods.backup_all");
        if (BtnUninstallAll != null) BtnUninstallAll.Content = t.T("mods.uninstall_all");
        if (DetailHeaderText != null) DetailHeaderText.Text = t.T("mods.detail_header");
        if (CoverBtn != null) { CoverBtn.Content = " " + t.T("mods.set_cover"); CoverBtn.ToolTip = t.T("mods.cover_tooltip"); }
        if (CategoryHeaderText != null) CategoryHeaderText.Text = t.T("mods.categories");
        if (DropHintText != null) DropHintText.Text = t.T("mods.drop_hint");

        // TODO(v1.x): Language refresh currently rebuilds the category list.
        // Consider separating data loading from display refresh.
        LoadCategories();
    }

}
