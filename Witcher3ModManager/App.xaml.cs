using System.Windows;
using System.Windows.Threading;
using Witcher3ModManager.Services;

namespace Witcher3ModManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global UI exception handler
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                string.Format(TranslationService.Instance.T("error.ui_crash"), args.Exception.Message, args.Exception.StackTrace?.Substring(0, Math.Min(args.Exception.StackTrace?.Length ?? 0, 500)) ?? ""),
                TranslationService.Instance.T("dialog.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Background thread exception handler
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    string.Format(TranslationService.Instance.T("error.fatal_msg"), ex.Message),
                    TranslationService.Instance.T("error.fatal"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }
}
