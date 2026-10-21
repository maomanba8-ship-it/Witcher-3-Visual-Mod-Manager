using System.IO;

namespace Witcher3ModManager.Core.Import;

/// <summary>
/// The folder to install when a package's structure could not be identified.
///
/// A single wrapper folder is what the user sees as "the mod folder", so that is what gets
/// installed. Anything else means the extraction root itself is the package. The one exception is a
/// wrapper named after a structural marker (content/scripts/metadata/bin): installing that would
/// drop a level, and the game would not find the files underneath it.
///
/// This is the explicit manual path — the user is asked before it is used — which is why it may
/// return a folder nothing else recognises.
/// </summary>
public static class ManualInstallRoot
{
    private static readonly string[] StructuralMarkers = { "content", "scripts", "metadata", "bin" };

    public static string? From(string? extractDir)
    {
        if (string.IsNullOrWhiteSpace(extractDir)) return null;

        DirectoryInfo root;
        try
        {
            root = new DirectoryInfo(extractDir);
            if (!root.Exists) return null;
        }
        catch
        {
            return null;
        }

        var subdirs = root.GetDirectories();
        var files = root.GetFiles();
        if (subdirs.Length == 0 && files.Length == 0) return null;

        if (subdirs.Length == 1 && files.Length == 0 && !IsStructuralMarker(subdirs[0].Name))
            return subdirs[0].FullName;

        return root.FullName;
    }

    public static bool IsStructuralMarker(string? name) =>
        StructuralMarkers.Contains(name ?? "", StringComparer.OrdinalIgnoreCase);
}
