using System.IO;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// What a folder in the game directory has to look like to be a mod's own folder.
///
/// The rule used to be "it has a content folder", which missed mods that ship only a bin payload —
/// they are real mods, and not noticing them means the library never learns they exist. An empty
/// folder of either name is not a payload, so something has to be in it.
/// </summary>
public static class GameFolderShape
{
    public static bool IsModFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;

        try
        {
            if (Directory.Exists(Path.Combine(path, "content"))) return true;

            var bin = Path.Combine(path, "bin");
            return Directory.Exists(bin) && Directory.EnumerateFileSystemEntries(bin).Any();
        }
        catch
        {
            // A folder that cannot be listed is not something to claim as a mod.
            return false;
        }
    }
}
