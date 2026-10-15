using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Witcher3ModManager.Services;

namespace Witcher3ModManager.Views;

public partial class HomePage : UserControl
{
    public event Action<string>? NavigateRequested;

    public HomePage()
    {
        InitializeComponent();
        VersionBadgeText.Text = Core.AppInfo.DisplayVersion;
        Loaded += (_, _) =>
        {
            TranslationService.Instance.LanguageChanged += () => Dispatcher.Invoke(RefreshLanguage);
            RefreshLanguage();
        };
    }

    private void LangToggle_Click(object sender, MouseButtonEventArgs e)
    {
        var ts = TranslationService.Instance;
        var next = ts.CurrentLanguage == "zh-CN" ? "en" : "zh-CN";
        ts.SetLanguage(next);
    }

    private void LangToggle_MouseEnter(object sender, MouseEventArgs e)
    {
        LangToggle.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8BA4B5"));
        LangToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#401C1F2A"));
        LangToggleText.Foreground = new SolidColorBrush(Colors.White);
    }

    private void LangToggle_MouseLeave(object sender, MouseEventArgs e)
    {
        LangToggle.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2AFFFFFF"));
        LangToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1814161E"));
        LangToggleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8D0D8"));
    }

    private void RefreshLanguage()
    {
        var ts = TranslationService.Instance;
        LangToggleText.Text = ts.CurrentLanguage == "zh-CN" ? "中" : "EN";
        BtnSettings.ToolTip = ts.T("home.settings");
        BtnMods.ToolTip = ts.T("home.tooltip_mods");
        BtnConflict.ToolTip = ts.T("home.tooltip_conflict");
        BtnPresets.ToolTip = ts.T("home.tooltip_presets");
        BtnImport.ToolTip = ts.T("home.tooltip_import");
        BtnLaunch.ToolTip = ts.T("home.tooltip_launch");
        BtnMods.Content = ts.T("home.mods");
        BtnConflict.Content = ts.T("home.conflicts");
        BtnPresets.Content = ts.T("home.presets");
        BtnImport.Content = ts.T("home.import");
        BtnLaunch.Content = ts.T("home.launch");
        BtnSettings.Content = ts.T("home.settings");
        WelcomeTitle.Text = ts.T("home.welcome_title");
        WelcomeSubtitle.Text = ts.T("home.welcome_subtitle");
    }

    private void BtnMods_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("mods");
    private void BtnConflict_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("conflicts");
    private void BtnPresets_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("presets");
    private void BtnImport_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("import");
    private void BtnLaunch_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("launch");
    private void BtnSettings_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("settings");

}
