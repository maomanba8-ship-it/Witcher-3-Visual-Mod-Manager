using System.IO;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Decides whether a path is something an operation may remove, or a directory the manager shares
/// between packages.
///
/// The distinction is not cosmetic. A bin-only package is recorded with the storage root itself in
/// InstalledPath, and the delete path used to hand that path to a recursive delete — which took the
/// storage root with every other package's copy inside it. The shared directories are never a
/// component, so they are never removable, whatever a record claims.
/// </summary>
public static class RemovableTarget
{
    /// <summary>
    /// True when the path is a package's own folder or file. A path that <em>is</em> one of the
    /// shared roots, and an empty or unusable path, are both refused.
    /// </summary>
    public static bool IsComponent(string? path, IEnumerable<string?>? sharedRoots)
    {
        var full = FullPath(path);
        if (full.Length == 0) return false;

        return !Normalize(sharedRoots).Contains(full, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The path as the file system would spell it, so "…\mods", "…\mods\" and "…\mods\." all
    /// compare equal. An unusable path comes back trimmed rather than throwing: refusing to remove
    /// it is the caller's decision, not this one's.
    /// </summary>
    public static string FullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd('\\', '/');
        }
    }

    private static IEnumerable<string> Normalize(IEnumerable<string?>? roots) =>
        (roots ?? Array.Empty<string?>())
            .Select(FullPath)
            .Where(r => r.Length > 0);
}
