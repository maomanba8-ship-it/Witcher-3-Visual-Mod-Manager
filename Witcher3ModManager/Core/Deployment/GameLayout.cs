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
    /// <summary>Executable the game boots from.</summary>
    public const string GameExecutableName = "witcher3.exe";

    /// <summary>
    /// Subfolders of "bin" that are tried first when a build ships more than one, newest naming
    /// first. This is only a preference: any other subfolder name is still found by the scan
    /// below, so a future layout rename does not need a code change here.
    /// </summary>
    private static readonly string[] PreferredBinSubfolders = { "x64_dx12", "x64" };

    /// <summary>How far above the path the user gave us we look for an installation root.</summary>
    private const int MaxRootSearchDepth = 4;

    /// <summary>
    /// Absolute path of the game executable for whatever the caller has: the game root, its
    /// "bin" folder, a bin subfolder, or the executable itself. Returns null when nothing in
    /// that chain looks like an installation.
    /// </summary>
    public static string? ResolveGameExecutable(string? userPath)
    {
        var root = FindGameRoot(userPath);
        return root is null ? null : FindExecutableUnder(root);
    }

    /// <summary>True when the given path is itself an installation root holding the game.</summary>
    public static bool IsGameRoot(string? path) =>
        !string.IsNullOrWhiteSpace(path) && FindExecutableUnder(path) is not null;

    /// <summary>
    /// Walks up from the given path until a folder that holds the game executable is found, so
    /// the user may point at the installation root, its bin folder, a bin subfolder, or the exe.
    /// Returns null when no ancestor qualifies.
    /// </summary>
    public static string? FindGameRoot(string? userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath)) return null;

        var current = File.Exists(userPath)
            ? Path.GetDirectoryName(userPath)
            : userPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (var depth = 0; depth < MaxRootSearchDepth && !string.IsNullOrEmpty(current); depth++)
        {
            if (FindExecutableUnder(current) is not null) return current;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = parent;
        }
        return null;
    }

    /// <summary>
    /// Finds the executable inside one candidate root, without walking any further up. Layouts
    /// are recognised structurally: directly inside "bin", or inside any subfolder of "bin"
    /// regardless of its name. The executable never sits at the top of a real installation, and
    /// requiring the "bin" level is what keeps a bin subfolder from being mistaken for the root.
    /// </summary>
    private static string? FindExecutableUnder(string root)
    {
        var bin = Path.Combine(root, "bin");
        if (!Directory.Exists(bin)) return null;

        var inBin = Path.Combine(bin, GameExecutableName);
        if (File.Exists(inBin)) return inBin;

        foreach (var hint in PreferredBinSubfolders)
        {
            var preferred = Path.Combine(bin, hint, GameExecutableName);
            if (File.Exists(preferred)) return preferred;
        }

        // Any other layout: scan the immediate subfolders of bin, in a stable order, so a renamed
        // or newly added subfolder keeps working without touching this file.
        foreach (var sub in Directory.EnumerateDirectories(bin).OrderBy(d => d, StringComparer.Ordinal))
        {
            var candidate = Path.Combine(sub, GameExecutableName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
