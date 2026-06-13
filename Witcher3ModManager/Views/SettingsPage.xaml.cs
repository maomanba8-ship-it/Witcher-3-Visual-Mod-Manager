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

    public SettingsPage()
    {
        InitializeComponent();
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
            ? "（使用默认背景）" : s.BackgroundImagePath;
        GamePathBox.Text = string.IsNullOrEmpty(s.Witcher3GamePath) 
            ? "（未设置）" : s.Witcher3GamePath;
        SevenZipBox.Text = s.SevenZipPath;
        ModStorageBox.Text = string.IsNullOrEmpty(s.ModStoragePath)
            ? "（使用默认路径）" : s.ModStoragePath;
        BackupPathBox.Text = string.IsNullOrEmpty(s.BackupPath)
            ? "（使用默认路径）" : s.BackupPath;
        ScriptMergerBox.Text = string.IsNullOrEmpty(s.ScriptMergerPath)
            ? "（未设置）" : s.ScriptMergerPath;
        LaunchArgsBox.Text = s.LaunchArgs;
        AutoBackupToggle.IsChecked = s.AutoBackup;
    }

    private void BrowseBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择背景图片",
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
        BgPathBox.Text = "（使用默认背景）";
        BackgroundImageChanged?.Invoke("");
    }

    private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 witcher3.exe（位于游戏目录\\bin\\x64 或 \\bin\\x64_dx12 下）",
            Filter = "witcher3.exe|witcher3.exe|可执行文件|*.exe"
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            // Navigate up from the exe to find the game root
            // witcher3.exe is typically at: [GameRoot]\bin\x64[_dx12]\witcher3.exe
            var exeDir = Path.GetDirectoryName(dlg.FileName) ?? "";
            var gameRoot = ResolveGameRoot(exeDir);
            
            _settings.SetGamePath(gameRoot);
            GamePathBox.Text = gameRoot;
            MessageBox.Show($"游戏根目录已设置为:\n{gameRoot}", "路径已设置",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Browse7Zip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 7z.exe",
            Filter = "7z.exe|7z.exe|可执行文件|*.exe"
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            _settings.SetSevenZipPath(dlg.FileName);
            SevenZipBox.Text = dlg.FileName;
        }
    }

    private void BrowseModStorage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择模组存储文件夹"
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
            Title = "选择备份文件夹"
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
            Title = "选择 WitcherScriptMerger.exe",
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
    private static string ResolveGameRoot(string exeDir)
    {
        // witcher3.exe is in [GameRoot]\bin\x64_dx12\ or [GameRoot]\bin\x64\
        // Go up until we find the "content" folder or "bin" as a subfolder
        var dir = exeDir;
        for (int i = 0; i < 5; i++)
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent == null) break;
            
            // Check if this looks like game root (has content/ or mods/ folders)
            if (Directory.Exists(Path.Combine(parent, "content")) ||
                Directory.Exists(Path.Combine(parent, "bin")))
            {
                return parent;
            }
            dir = parent;
        }
        // Fallback: use the original path
        return exeDir;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBackRequested?.Invoke();
    }
}
