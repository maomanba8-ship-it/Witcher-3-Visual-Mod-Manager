using System.IO;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Picks the first usable path from an ordered candidate list. The caller decides the order
/// (a path the user configured first, then the usual install locations) and this only reports
/// which candidate actually exists, so no single location is ever hardcoded as the answer and
/// the choice is testable without installing anything.
/// </summary>
public static class ToolLocator
{
    public static string? FirstExisting(IEnumerable<string?>? candidates)
    {
        if (candidates is null) return null;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Locates <c>WitcherScriptMerger.exe</c>: the configured path first, then the usual install
    /// locations. Shared by the conflict dialog and the merge-inventory lookup, so both always
    /// refer to the same installation.
    /// </summary>
    public static string? FindScriptMerger(string? configuredPath) =>
        FirstExisting(new[]
        {
            configuredPath,
            @"D:\Tools\WitcherScriptMerger\WitcherScriptMerger.exe",
            @"C:\Program Files\WitcherScriptMerger\WitcherScriptMerger.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "WitcherScriptMerger", "WitcherScriptMerger.exe")
        });
}
