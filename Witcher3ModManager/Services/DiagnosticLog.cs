using System.Diagnostics;
using System.IO;
using Witcher3ModManager.Core.Diagnostics;

namespace Witcher3ModManager.Services;

/// <summary>
/// DEBUG-only diagnostic sink. It writes under the application data folder, so a test build
/// never needs write access to the installation directory, and it never throws: a failed log
/// line must never be able to break an import.
///
/// Marked [Conditional("DEBUG")] rather than wrapped in #if so that the method exists in every
/// configuration. Call sites then need no conditional compilation of their own, and an exception
/// handed to the log is still referenced in Release, where an unused catch variable would warn.
/// </summary>
internal static class DiagnosticLog
{
    private static readonly object Gate = new();

    [Conditional("DEBUG")]
    public static void Write(string message)
    {
        try
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDataPath.FolderName);
            var logPath = AppDiagnostics.LogFilePath(appDataRoot);

            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Best effort by design: diagnostics are not one of the operations that must report
            // a structured error, and failing to write a log line must not fail the user's action.
        }
    }
}
