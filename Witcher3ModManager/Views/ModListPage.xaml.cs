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
    public event Action<int, int, int, int>? StatusUpdated;  // enabled, total, conflicts, potentialOverrides

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
            var display = count > 0 ? $"{cat.DisplayName} ({count})" : cat.DisplayName;
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

    private void ApplyCategoryFilter()
    {
        var filtered = _selectedCategory == null || _selectedCategory.Type == CategoryType.All
            ? _allMods
            : _allMods.Where(m => m.Category == _selectedCategory.Type).ToList();

        // Build UI cards in code (NO BAML!)
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var mod in filtered)
            panel.Children.Add(CreateModCard(mod));

        ModItemsControl.ItemsSource = null;
        ModItemsControl.Items.Clear();
        ModItemsControl.Items.Add(new ContentPresenter { Content = panel });

        WelcomePanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModListScroll.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border CreateModCard(ModEntry mod)
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
        try { conflicts = _conflict?.DetectConflicts().Count ?? 0; } catch { }
        var overrides = _conflict?.DetectPotentialOverrideCount() ?? 0;
        StatusUpdated?.Invoke(enabled, total, conflicts, overrides);
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
            var imported = await _importer.ImportArchiveAsync(filePath);
            if (imported.Count > 0) 
            {
                _conflict?.InvalidateCache();
                RefreshModList();
                if (imported.Count > 1)
                    MessageBox.Show($"导入了 {imported.Count} 个模组（组合包已拆分）",
                        "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else MessageBox.Show("导入失败: " + Path.GetFileName(filePath) + "\n\n"
                    + "该模组使用了 Bundle 补丁方式（quickbms），不是标准模组格式。\n"
                    + "请手动解压后运行其中的 Install.bat 进行安装。",
                    "导入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Import error: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var confirmed = ConfirmDialog.Show("确认删除",
                "Delete [" + mod.Name + "]?\n\nThis cannot be undone.",
                "\uE74D", "Delete", "取消");
            if (!confirmed) return;
            _conflict?.InvalidateCache();
            if (_w3 != null) _w3.DeleteMod(mod);
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
            var setCoverItem = new MenuItem { Header = "\uE722  设置预览图片" };
            setCoverItem.Click += (s, args) => SetCoverImage(mod);
            menu.Items.Add(setCoverItem);

            var clearCoverItem = new MenuItem { Header = "\uE74D  清除封面" };
            clearCoverItem.Click += (s, args) => { mod.CoverImagePath = ""; _storage?.UpdateMod(mod); ShowModDetail(mod); };
            menu.Items.Add(clearCoverItem);

            menu.IsOpen = true;
        }
    }

    private void SetCoverImage(ModEntry mod)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择预览图片",
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
        DetailOriginalName.Text = mod.GameModFolders.Count > 0 ? "文件名： " + string.Join(", ", mod.GameModFolders) : "";
        RenameBtn.Tag = mod;
        DetailAuthor.Text = "作者： " + mod.Author;
        DetailVersion.Text = "版本： " + mod.Version;
        DetailCategory.Text = "分类： " + mod.DisplayCategory + " | " + mod.DisplayModType;
        DetailPriority.Text = mod.Priority.ToString();
        DetailImportDate.Text = "安装日期： " + mod.ImportDate.ToString("yyyy-MM-dd HH:mm");
        try
        {
            if (Directory.Exists(mod.InstalledPath))
            {
                var size = new DirectoryInfo(mod.InstalledPath)
                    .GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                DetailSize.Text = string.Format("大小： {0:F2} MB", size / 1024.0 / 1024.0);
            }
        }
        catch { DetailSize.Text = "大小： 未知"; }
        DetailLocation.Text = mod.InstalledPath;
        DetailDescription.Text = string.IsNullOrEmpty(mod.Description) ? "（无描述）" : mod.Description;
        // Warn if storage directory is missing
        if (!string.IsNullOrEmpty(mod.InstalledPath) && !Directory.Exists(mod.InstalledPath))
            DetailDescription.Text = "⚠ 存储目录已丢失！\n" + DetailDescription.Text;
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
                    MessageBox.Show("启用失败： " + mod.Name, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                _w3.DisableMod(mod);
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
        
        var panel2 = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var mod in filtered) panel2.Children.Add(CreateModCard(mod));
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
        var dialog = new ConflictDialog(conflicts, modDateMap, overrides);
        dialog.ShowDialog();
        UpdateStatusBar();
    }

    private void BackupAll_Click(object sender, RoutedEventArgs e)
    {
        if (_backup == null) return;
        var results = _backup.BackupAllEnabledMods(_allMods);
        var count = results.Count(r => r.Success);
        MessageBox.Show("已备份 " + count + " 个模组。", "备份", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UninstallAll_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = ConfirmDialog.Show("全部卸载",
            "卸载所有已启用模组？\n\n这将禁用并从游戏中移除它们。",
            "\uEA3B", "卸载", "取消");
        if (!confirmed) return;
        if (_backup != null) _backup.UninstallAllMods(_allMods);
        _conflict?.InvalidateCache();
        RefreshModList();
        MessageBox.Show("所有模组已卸载。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CoverBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择预览图片",
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
            var newName = InputDialog.Show("编辑显示名", "\uE8EC",
                "输入自定义显示名称（不影响原始文件）：",
                mod.UserDisplayName ?? mod.Name,
                "确定", "取消");
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
}
