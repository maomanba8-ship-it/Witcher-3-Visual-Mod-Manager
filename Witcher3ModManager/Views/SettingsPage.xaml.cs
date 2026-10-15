using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Witcher3ModManager.Models;
using Witcher3ModManager.Services;

namespace Witcher3ModManager.Views;

public partial class SettingsPage : UserControl
{
    private SettingsService? _settings;
    public event Action? NavigateBackRequested;
    public event Action<string>? BackgroundImageChanged;
    private IDisposable? _languageSubscription;

    public SettingsPage()
    {
        InitializeComponent();
        _languageSubscription = TranslationService.Instance.Subscribe(() => Dispatcher.Invoke(RefreshLanguage));
        RefreshLanguage();
        Unloaded += (_, _) =>
        {
            _languageSubscription?.Dispose();
            _languageSubscription = null;
        };
    }

    public void Initialize(SettingsService settings)
    {
        _settings = settings;
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (_settings == null) return;

        var s = _settings.Current;
        BgPathBox.Text = string.IsNullOrEmpty(s.BackgroundImagePath) 
            ? TranslationService.Instance.T("settings.default_bg") : s.BackgroundImagePath;
        GamePathBox.Text = string.IsNullOrEmpty(s.Witcher3GamePath) 
            ? TranslationService.Instance.T("settings.not_set") : s.Witcher3GamePath;
        SevenZipBox.Text = s.SevenZipPath;
        ModStorageBox.Text = string.IsNullOrEmpty(s.ModStoragePath)
            ? TranslationService.Instance.T("settings.default_path") : s.ModStoragePath;
        BackupPathBox.Text = string.IsNullOrEmpty(s.BackupPath)
            ? TranslationService.Instance.T("settings.default_path") : s.BackupPath;
        ScriptMergerBox.Text = string.IsNullOrEmpty(s.ScriptMergerPath)
            ? TranslationService.Instance.T("settings.not_set") : s.ScriptMergerPath;
        LaunchArgsBox.Text = s.LaunchArgs;
        AutoBackupToggle.IsChecked = s.AutoBackup;
    }

    private void BrowseBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = TranslationService.Instance.T("settings.dlg_bg"),
            Filter = "图片文件|*.jpg;*.jpeg;*.png|所有文件|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _settings?.SetBackgroundImage(dlg.FileName);
            BgPathBox.Text = dlg.FileName;
            BackgroundImageChanged?.Invoke(dlg.FileName);
        }
    }

    private void ResetBg_Click(object sender, RoutedEventArgs e)
    {
        _settings?.SetBackgroundImage("");
        BgPathBox.Text = TranslationService.Instance.T("settings.default_bg");
        BackgroundImageChanged?.Invoke("");
    }

    private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = TranslationService.Instance.T("settings.dlg_game_path")
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            var selected = dlg.FolderName;
            if (!Directory.Exists(Path.Combine(selected, "content")))
            {
                MessageBox.Show(
                    TranslationService.Instance.T("settings.game_path_invalid_msg"),
                    TranslationService.Instance.T("settings.path_invalid"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.SetGamePath(selected);
            GamePathBox.Text = selected;
            MessageBox.Show(string.Format(TranslationService.Instance.T("settings.game_path_set"), selected), TranslationService.Instance.T("settings.path_set"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Browse7Zip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = TranslationService.Instance.T("settings.dlg_7zip"),
            Filter = "7z.exe|7z.exe|可执行文件|*.exe"
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            if (!IsValid7Zip(dlg.FileName))
            {
                MessageBox.Show(TranslationService.Instance.T("settings.invalid_7zip"), TranslationService.Instance.T("settings.path_invalid"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.SetSevenZipPath(dlg.FileName);
            SevenZipBox.Text = dlg.FileName;
        }
    }

    private static bool IsValid7Zip(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                Arguments = "--help",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    private void BrowseModStorage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = TranslationService.Instance.T("settings.dlg_mod_storage")
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            _settings.Current.ModStoragePath = dlg.FolderName;
            _settings.Save();
            ModStorageBox.Text = dlg.FolderName;
        }
    }

    private void BrowseBackupPath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = TranslationService.Instance.T("settings.dlg_backup")
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            _settings.SetBackupPath(dlg.FolderName);
            BackupPathBox.Text = dlg.FolderName;
        }
    }

    private void BrowseScriptMerger_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = TranslationService.Instance.T("settings.dlg_script_merger"),
            Filter = "可执行文件|*.exe"
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            _settings.SetScriptMergerPath(dlg.FileName);
            ScriptMergerBox.Text = dlg.FileName;
        }
    }

    private void LaunchArgs_Changed(object sender, TextChangedEventArgs e)
    {
        _settings?.SetLaunchArgs(LaunchArgsBox.Text);
    }

    private void AutoBackup_Changed(object sender, RoutedEventArgs e)
    {
        _settings?.SetAutoBackup(AutoBackupToggle.IsChecked == true);
    }

    // Navigate up from exe directory to find the actual game root
    

    private void RefreshLanguage()
    {
        var ts = TranslationService.Instance;
        BackBtnText.Text = ts.T("settings.back");
        SettingsTitle.Text = ts.T("settings.title");
        AppearanceHeader.Text = ts.T("settings.appearance");
        CustomBgLabel.Text = ts.T("settings.custom_bg");
        BgHintText.Text = ts.T("settings.bg_hint");
        PathConfigHeader.Text = ts.T("settings.path_config");
        GamePathLabel.Text = ts.T("settings.game_path");
        SevenZipLabel.Text = ts.T("settings.zip_path");
        BackupPathLabel.Text = ts.T("settings.backup_path");
        ScriptMergerLabel.Text = ts.T("settings.script_merger");
        ModStorageLabel.Text = ts.T("settings.mod_storage");
        GameOptionsHeader.Text = ts.T("settings.game_options");
        LaunchArgsLabel.Text = ts.T("settings.launch_args");
        LaunchArgsHint.Text = ts.T("settings.launch_args_hint");
        ToolsHeader.Text = ts.T("settings.tools");
        AutoBackupCheckText.Text = ts.T("settings.auto_backup");
        AutoBackupDescText.Text = ts.T("settings.auto_backup_desc");
        MoreFeaturesHeader.Text = ts.T("settings.more");
        AboutHeader.Text = ts.T("settings.about");
        AboutVersionText.Text = ts.T("settings.about_text");
        AboutTechText.Text = ts.T("settings.about_tech");
        MoreFeature1Text.Text = ts.T("settings.more1");
        MoreFeature2Text.Text = ts.T("settings.more2");
        MoreFeature3Text.Text = ts.T("settings.more3");
        MoreFeature4Text.Text = ts.T("settings.more4");
        LaunchArgsBox.ToolTip = ts.T("settings.launch_args_tooltip");

        // Browse buttons
        BrowseBgBtn.Content = ts.T("settings.bg_browse");
        BrowseGameBtn.Content = ts.T("settings.bg_browse");
        BrowseZipBtn.Content = ts.T("settings.bg_browse");
        BrowseBackupBtn.Content = ts.T("settings.bg_browse");
        BrowseMergerBtn.Content = ts.T("settings.bg_browse");
        BrowseStorageBtn.Content = ts.T("settings.bg_browse");

        // ToolTips
        ResetBgBtn.ToolTip = ts.T("settings.bg_reset");
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBackRequested?.Invoke();
    }
}
