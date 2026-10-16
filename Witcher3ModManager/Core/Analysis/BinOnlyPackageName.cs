namespace Witcher3ModManager.Core.Analysis;

/// <summary>
/// Names a package that comes without a folder of its own ("pure bin": an archive whose bin/
/// folder sits at the top level).
///
/// Normally bin/ is inside the mod's own folder and that folder is the name. When it is not, the
/// only directory left to look at is the one this manager unpacked into — which is not a name at
/// all, and using it produced records called "_tmp_255a4158". The archive's own file name is the
/// honest answer, and it is the same shape a name has when it comes from anywhere else.
/// </summary>
public static class BinOnlyPackageName
{
    public static string From(string? binParentName, string? extractionDirectoryName, string? archiveName)
    {
        var parent = Clean(binParentName);
        if (parent.Length > 0
            && !string.Equals(parent, Clean(extractionDirectoryName), StringComparison.OrdinalIgnoreCase))
            return parent;

        var archive = Clean(archiveName);
        if (archive.Length > 0) return archive;

        // Nothing but the temporary folder to go on; a name from it beats no name at all.
        return parent.Length > 0 ? parent : Clean(extractionDirectoryName);
    }

    private static string Clean(string? name) => (name ?? "").Trim();
}
