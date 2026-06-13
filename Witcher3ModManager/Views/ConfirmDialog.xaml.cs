using System.Windows;
using System.Windows.Media;

namespace Witcher3ModManager.Views;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }
    private readonly Brush _confirmBrush;

    public ConfirmDialog(string title, string message, string iconGlyph = "\uEA3B",
        string confirmText = "确认", string cancelText = "取消",
        Color? confirmColor = null)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        TitleText.Text = title;
        MessageText.Text = message;
        TitleIcon.Text = iconGlyph;
        ConfirmBtn.Content = confirmText;
        CancelBtn.Content = cancelText;

        _confirmBrush = new SolidColorBrush(confirmColor ?? 
            (Color)ColorConverter.ConvertFromString("#E0556A"));
        ConfirmBtn.Background = _confirmBrush;
    }

    public static bool Show(string title, string message,
        string iconGlyph = "\uEA3B", string confirmText = "确认",
        string cancelText = "取消", Color? confirmColor = null)
    {
        var color = confirmColor ?? (Color)ColorConverter.ConvertFromString("#E0556A");
        var dlg = new ConfirmDialog(title, message, iconGlyph, confirmText, cancelText, color);
        dlg.ShowDialog();
        return dlg.Confirmed;
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
