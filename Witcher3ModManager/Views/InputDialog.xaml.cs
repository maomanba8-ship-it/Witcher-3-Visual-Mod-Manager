using System.Windows;
using System.Windows.Input;

namespace Witcher3ModManager.Views;

public partial class InputDialog : Window
{
    public string InputText { get; private set; } = "";
    public bool Confirmed { get; private set; }

    public InputDialog(string title, string iconGlyph, string prompt, string defaultText = "",
        string confirmText = "OK", string cancelText = "Cancel")
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        TitleText.Text = title;
        TitleIcon.Text = iconGlyph;
        InputBox.Text = defaultText;
        ConfirmBtn.Content = confirmText;
        CancelBtn.Content = cancelText;

        InputBox.Focus();
        if (!string.IsNullOrEmpty(defaultText))
            InputBox.SelectAll();
    }

    public static string? Show(string title, string iconGlyph = "\uE8EC",
        string prompt = "", string defaultText = "",
        string confirmText = "OK", string cancelText = "Cancel")
    {
        var dlg = new InputDialog(title, iconGlyph, prompt, defaultText, confirmText, cancelText);
        dlg.ShowDialog();
        return dlg.Confirmed ? dlg.InputText : null;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirmed = true;
            InputText = InputBox.Text;
            Close();
        }
        else if (e.Key == Key.Escape)
        {
            Confirmed = false;
            Close();
        }
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        InputText = InputBox.Text;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}