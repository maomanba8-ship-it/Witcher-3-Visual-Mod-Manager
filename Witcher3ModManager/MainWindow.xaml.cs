using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Witcher3ModManager.Models;
using Witcher3ModManager.Services;
using Witcher3ModManager.Views;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager;

public partial class MainWindow : Window
{
    private HomePage? _homePage;
    private ModListPage? _modListPage;
    private SettingsPage? _settingsPage;
    private ModStorageService? _storage;
    private ModImportService? _importer;
    private SettingsService? _settings;
    private Witcher3Service? _w3;
    private ConflictService? _conflict;
    private BackupService? _backup;
    private PresetPage? _presetPage;
    private PresetService? _presets;
    private OrphanDetectionService? _orphanDetector;
    private IDisposable? _languageSubscription;

    public MainWindow()
    {
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Services.TranslationService.Instance.T("error.ui_init"), ex.Message), Services.TranslationService.Instance.T("dialog.error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
            return;
        }
        Loaded += OnLoaded;
        if (AppDataPath.IsTestMode) ApplyTestMode();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _storage = new ModStorageService();
            foreach (var warning in _storage.GetLegacyDataWarnings())
            {
                System.Diagnostics.Debug.WriteLine("[W3MM] 数据警告: " + warning);
            }
            ReportLibraryLoadOutcome();
            _settings = new SettingsService();
            _importer = new ModImportService(_storage, _settings);
            _w3 = new Witcher3Service(_settings, _storage);
            _conflict = new ConflictService(_storage, _w3);
            _backup = new BackupService(_w3, _settings);
            _presets = new PresetService();
            _orphanDetector = new OrphanDetectionService();

            LoadTitleIcon();
            LoadBackgroundImage();
            DetectAndShowGamePath();
            NavigateToHome();

            // Language switching
            _languageSubscription = TranslationService.Instance.Subscribe(() => Dispatcher.Invoke(() =>
            {
                TitleText.Text = TranslationService.Instance.T("app.title");
                BtnMinimize.ToolTip = TranslationService.Instance.T("window.minimize");
                BtnMaximize.ToolTip = TranslationService.Instance.T("window.maximize");
                BtnClose.ToolTip = TranslationService.Instance.T("window.close");
            }));
            Closed += (_, _) => _languageSubscription?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Services.TranslationService.Instance.T("error.startup"), ex.Message), Services.TranslationService.Instance.T("dialog.error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadTitleIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Icons", "app_icon.png");
            if (File.Exists(iconPath))
            {
                var bmp = new BitmapImage(new Uri(iconPath));
                bmp.Freeze();
                TitleIcon.Source = bmp;
            }
        }
        catch { }
    }

    private void LoadBackgroundImage()
    {
        string? bgPath = null;
        try
        {
            if (_settings != null && !string.IsNullOrEmpty(_settings.Current.BackgroundImagePath))
            {
                var custom = _settings.Current.BackgroundImagePath;
                if (File.Exists(custom)) bgPath = custom;
            }
        }
        catch { }

        if (bgPath == null)
        {
            var defPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "bg_default.jpg");
            if (File.Exists(defPath)) bgPath = defPath;
        }

        if (bgPath != null)
        {
            try
            {
                var bmp = new BitmapImage(new Uri(bgPath));
                bmp.Freeze();
                BgImageClear.Source = bmp;
            }
            catch { }
        }
    }

    private void DetectAndShowGamePath()
    {
        try
        {
            var path = _w3?.GetGamePath();
            var ts = Services.TranslationService.Instance;
            StatusGamePath.Text = path != null
                ? string.Format(ts.T("status.game_path"), Path.GetFileName(path))
                : ts.T("status.game_not_detected");
        }
        catch { StatusGamePath.Text = Services.TranslationService.Instance.T("status.game_not_detected"); }
    }

    private void NavigateToHome()
    {
        _homePage ??= new HomePage();
        _homePage.NavigateRequested -= OnHomeNavigate;
        _homePage.NavigateRequested += OnHomeNavigate;
        PageHost.Content = _homePage;
        SetBackgroundBlur(false);
        MainStatusBar.Visibility = Visibility.Collapsed;
    }

    private void OnHomeNavigate(string target)
    {
        switch (target)
        {
            case "mods":
            case "conflicts":
                NavigateToModList();
                break;
            case "import":
                NavigateToModList();
                _modListPage?.ImportButton_Click(this, new RoutedEventArgs());
                break;
            case "settings":
                NavigateToSettings();
                break;
            case "launch":
                LaunchGame();
                break;
            case "presets":
                NavigateToPresets();
                break;
        }
    }

    private void NavigateToModList()
    {
        if (_storage == null || _importer == null || _w3 == null || _conflict == null || _backup == null || _settings == null) return;
        _modListPage ??= new ModListPage();
        _modListPage.NavigateBackRequested -= OnModListBack;
        _modListPage.NavigateBackRequested += OnModListBack;
        _modListPage.StatusUpdated -= OnStatusUpdated;
        _modListPage.StatusUpdated += OnStatusUpdated;
        _modListPage.Initialize(_storage, _importer, _w3, _conflict, _backup, _settings);
        if (_orphanDetector != null)
        {
            var gamePath = _w3?.GetGamePath();
            if (gamePath != null)
            {
                var orphans = _orphanDetector.ScanOrphans(gamePath, _storage.GetAllMods());
                _modListPage.SetOrphans(orphans);
            }
        }
        PageHost.Content = _modListPage;
        SetBackgroundBlur(true);
        MainStatusBar.Visibility = Visibility.Visible;
        RefreshStatusBar();
    }

    private void NavigateToPresets()
    {
        if (_presets == null || _storage == null || _w3 == null) return;
        _presetPage ??= new PresetPage();
        _presetPage.NavigateBackRequested -= OnPresetsBack;
        _presetPage.NavigateBackRequested += OnPresetsBack;
        _presetPage.PresetApplied -= OnPresetApplied;
        _presetPage.PresetApplied += OnPresetApplied;
        _presetPage.Initialize(_presets, _storage, _w3);
        PageHost.Content = _presetPage;
        SetBackgroundBlur(true);
        MainStatusBar.Visibility = Visibility.Collapsed;
    }

    private void OnPresetsBack() => NavigateToHome();

    private void OnPresetApplied()
    {
        _modListPage?.RefreshModList();
    }

    private void NavigateToSettings()
    {
        if (_settings == null) return;
        _settingsPage ??= new SettingsPage();
        _settingsPage.NavigateBackRequested -= OnSettingsBack;
        _settingsPage.NavigateBackRequested += OnSettingsBack;
        _settingsPage.BackgroundImageChanged -= OnBackgroundChanged;
        _settingsPage.BackgroundImageChanged += OnBackgroundChanged;
        _settingsPage.Initialize(_settings);
        PageHost.Content = _settingsPage;
        SetBackgroundBlur(true);
        MainStatusBar.Visibility = Visibility.Collapsed;
    }

    private void LaunchGame()
    {
        if (_w3 == null) return;
        var path = _w3.GetGamePath();
        if (path == null)
        {
            MessageBox.Show(TranslationService.Instance.T("game.not_detected"),
                TranslationService.Instance.T("dialog.hint"), MessageBoxButton.OK, MessageBoxImage.Information);
            NavigateToSettings();
            return;
        }
        try { _w3.LaunchGame(); }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Services.TranslationService.Instance.T("error.startup"), ex.Message), Services.TranslationService.Instance.T("dialog.error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnModListBack() => NavigateToHome();
    private void OnSettingsBack() => NavigateToHome();

    private void OnBackgroundChanged(string path) => LoadBackgroundImage();

    private void OnStatusUpdated(int enabled, int total, int conflicts, int potential, int bundleCount, int sharedCount)
    {
        StatusEnabled.Text = string.Format(Services.TranslationService.Instance.T("status.enabled"), enabled);
        StatusTotal.Text = string.Format(Services.TranslationService.Instance.T("status.total"), total);
        StatusConflicts.Text = string.Format(Services.TranslationService.Instance.T("status.conflicts"), conflicts);
        StatusPotential.Text = string.Format(Services.TranslationService.Instance.T("status.potential"), potential);
        StatusBundle.Text = string.Format(Services.TranslationService.Instance.T("status.bundle"), bundleCount);
        StatusShared.Text = string.Format(Services.TranslationService.Instance.T("status.shared"), sharedCount);
    }

    private void ConflictStatus_Click(object sender, RoutedEventArgs e)
    {
        OpenConflictDialog();
    }

    private void SharedStatus_Click(object sender, RoutedEventArgs e) => OpenConflictDialog();

    private void BundleStatus_Click(object sender, RoutedEventArgs e) => OpenConflictDialog();

    /// <summary>
    /// Opens the conflict dialog with the complete picture. The three status-bar entries
    /// (conflicts, shared resources, bundle mods) all open the same dialog, so they share this
    /// one code path instead of one of them reaching into the mod list page by reflection.
    /// </summary>
    private void OpenConflictDialog()
    {
        if (_conflict == null || _storage == null) return;

        var conflicts = _conflict.ForceDetectConflicts();
        // GroupBy keeps duplicate names safe (e.g. from incomplete Mod+DLC merges).
        var modDateMap = _storage.GetAllMods()
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.First().ImportDate);
        var overrides = _conflict.GetPotentialOverrides();
        var sharedFolders = _conflict.GetSharedFolderPairs();
        var bundleMods = _conflict.GetBundleMods();
        var sharedContentDiffs = _conflict.GetSharedContentDiffs().Select(d => d.Folder);
        var retainedBinFiles = _conflict.GetRetainedBinFiles();
        var modPriorities = _storage.GetAllMods()
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.First().Priority);

        var dialog = new Views.ConflictDialog(
            conflicts, modDateMap, overrides, bundleMods, sharedFolders, sharedContentDiffs,
            _settings, retainedBinFiles, modPriorities);
        dialog.ShowDialog();
        RefreshStatusBar();
    }

    private void PotentialStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_conflict == null) return;
        var overlaps = _conflict.GetPotentialOverrides();
        if (overlaps.Count == 0)
        {
            System.Windows.MessageBox.Show(Services.TranslationService.Instance.T("potential.no_overlaps"),
                Services.TranslationService.Instance.T("potential.title"), System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }
        var dialog = new Views.PotentialOverlapDialog(overlaps);
        dialog.ShowDialog();
    }

    private void RefreshStatusBar()
    {
        if (_storage == null || _conflict == null) return;
        var allMods = _storage.GetAllMods();
        // Route through the same handler the mod list uses, so every counter in the bar is
        // refreshed here too and the two paths cannot drift apart.
        OnStatusUpdated(
            allMods.Count(m => m.IsEnabled),
            allMods.Count,
            _conflict.DetectConflicts().Count(c => ConflictEvidenceRules.IsRealConflictKind(c.Kind)),
            _conflict.DetectPotentialOverrideCount(),
            _conflict.GetBundleMods().Count,
            _conflict.GetSharedResources().Count);
    }



    private void SetBackgroundBlur(bool blurred)
    {
        if (blurred)
        {
            BgImageClear.Visibility = Visibility.Collapsed;
            BgDarkOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            BgImageClear.Visibility = Visibility.Visible;
            BgDarkOverlay.Visibility = Visibility.Collapsed;
        }
    }



    /// <summary>
    /// A damaged or unreadable library has to reach the user: the list on screen is not the one
    /// they left behind, and only they can decide what to do about it.
    /// </summary>
    private void ReportLibraryLoadOutcome()
    {
        if (_storage == null) return;

        var t = Services.TranslationService.Instance;
        var message = _storage.LibraryLoadOutcome switch
        {
            Core.Storage.TextFileOutcome.RestoredFromBackup =>
                string.Format(t.T("storage.library_restored"), _storage.DamagedLibraryCopyPath ?? "-"),
            Core.Storage.TextFileOutcome.Damaged =>
                string.Format(t.T("storage.library_damaged"), _storage.DamagedLibraryCopyPath ?? "-"),
            Core.Storage.TextFileOutcome.ReadFailed => t.T("storage.library_read_failed"),
            _ => (string?)null
        };

        if (message != null)
            MessageBox.Show(message, t.T("dialog.warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ApplyTestMode()
    {
        TitleText.Text = TranslationService.Instance.T("app.title") + " [TEST]";
        TestBanner.Visibility = Visibility.Visible;
    }

    #region Window Controls
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.ChangedButton == MouseButton.Left) DragMove();
    }
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            BtnMaximize.Content = "\uE922";
        }
        else
        {
            WindowState = WindowState.Maximized;
            BtnMaximize.Content = "\uE923";
        }
    }
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    #endregion
}
