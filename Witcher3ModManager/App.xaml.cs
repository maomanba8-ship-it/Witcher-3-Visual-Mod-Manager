using System.Windows;
using System.Windows.Threading;

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
                "程序发生未处理的错误：\n\n" + args.Exception.Message + 
                "\n\n" + args.Exception.StackTrace?.Substring(0, Math.Min(args.Exception.StackTrace?.Length ?? 0, 500)),
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Background thread exception handler
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    "程序发生严重错误：\n\n" + ex.Message,
                    "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }
}
