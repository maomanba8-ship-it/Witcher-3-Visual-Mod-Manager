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
}
