using System.IO;

namespace Witcher3ModManager.Core.Diagnostics;

/// <summary>
/// Where the application writes its own diagnostic logs. They must live under a folder the user
/// can write: an installation under Program Files is read-only, so logging next to the
/// executable fails silently. Kept as pure path arithmetic so the rule is testable without
/// touching the file system.
/// </summary>
public static class AppDiagnostics
{
    public const string LogFolderName = "logs";
    public const string LogFileName = "diagnostics.log";

    /// <summary>
    /// Full path of the diagnostic log for the given application data root. A blank root falls
    /// back to a bare relative file name, which still beats throwing inside a log call.
    /// </summary>
    public static string LogFilePath(string? appDataRoot)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot)) return LogFileName;
        return Path.Combine(appDataRoot, LogFolderName, LogFileName);
    }
}
