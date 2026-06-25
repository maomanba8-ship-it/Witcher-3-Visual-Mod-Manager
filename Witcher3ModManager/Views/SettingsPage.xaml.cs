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
        var dlg = new OpenFolderDialog
        {
            Title = "选择巫师3游戏根目录（包含 content 文件夹的目录）"
        };
        if (dlg.ShowDialog() == true && _settings != null)
        {
            var selected = dlg.FolderName;
            if (!Directory.Exists(Path.Combine(selected, "content")))
            {
                MessageBox.Show(
                    "所选目录不包含 content 文件夹，不是有效的巫师3游戏根目录。\n\n请选择 The Witcher 3 根目录（例如：Steam\\steamapps\\common\\The Witcher 3）",
                    "路径无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.SetGamePath(selected);
            GamePathBox.Text = selected;
            MessageBox.Show(string.Format("游戏根目录已设置为:\n{0}", selected), "路径已设置",
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
            if (!IsValid7Zip(dlg.FileName))
            {
                MessageBox.Show("所选文件不是有效的 7z.exe，请重新选择。", "路径无效",
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
    

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBackRequested?.Invoke();
    }
}
