using System.IO;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Pure, read-only rules for locating the game installation and its executable. The layout
/// depends on the game version: the classic build keeps the binary in bin\x64, while the newer
/// DX12-only build ships bin\x64_dx12 and has no bin\x64 at all. Both are recognised so a path
/// probe never depends on which version the user has installed. It never writes, moves or
/// deletes anything, and it special-cases no mod.
/// </summary>
public static class GameLayout
{
    /// <summary>Subfolders of "bin" that may hold witcher3.exe, newest layout first.</summary>
    public static readonly IReadOnlyList<string> BinSubfolders = new[] { "x64_dx12", "x64" };

    /// <summary>
    /// Absolute path of the game executable under the given game root, or null when no known
    /// layout matches. The newest layout wins when a build ships more than one.
    /// </summary>
    public static string? ResolveGameExecutable(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return null;

        foreach (var sub in BinSubfolders)
        {
            var candidate = Path.Combine(gamePath, "bin", sub, "witcher3.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>True when the directory looks like a game installation root.</summary>
    public static bool IsGameRoot(string? path) => ResolveGameExecutable(path) != null;

    /// <summary>
    /// When the user picked a bin subfolder instead of the game root, returns the root above it;
    /// otherwise null. Keeps the bin subfolder list in one place, so a future layout only has to
    /// be added to BinSubfolders.
    /// </summary>
    public static string? GameRootFromBinPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var sub in BinSubfolders)
        {
            if (normalized.EndsWith(Path.Combine("bin", sub), StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(Path.GetDirectoryName(normalized)) ?? normalized;
        }
        return null;
    }
}
