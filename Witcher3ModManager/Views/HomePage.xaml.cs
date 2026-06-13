using System.Windows;
using System.Windows.Controls;

namespace Witcher3ModManager.Views;

public partial class HomePage : UserControl
{
    public event Action<string>? NavigateRequested;

    public HomePage()
    {
        InitializeComponent();
    }

    private void BtnMods_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("mods");
    private void BtnConflict_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("conflicts");
    private void BtnPresets_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("presets");
    private void BtnImport_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("import");
    private void BtnLaunch_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("launch");
    private void BtnSettings_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("settings");
}
