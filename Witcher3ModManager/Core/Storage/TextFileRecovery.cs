using System.IO;

namespace Witcher3ModManager.Core.Storage;

/// <summary>
/// What happened when a data file was read.
/// </summary>
public enum TextFileOutcome
{
    /// <summary>Read and usable.</summary>
    Loaded,

    /// <summary>Nothing there yet; a first run, not a problem.</summary>
    Missing,

    /// <summary>The file was damaged and the previous version was used instead.</summary>
    RestoredFromBackup,

    /// <summary>The file was damaged and no usable backup existed.</summary>
    Damaged,

    /// <summary>The file exists but could not be read at all (locked, permissions, disk).</summary>
    ReadFailed
}

/// <summary>
/// Result of a recovery-aware read. Detail names the preserved copy of a damaged file, when one
/// was made, so the user can be told where their data went.
/// </summary>
public sealed record TextFileLoad(string? Text, TextFileOutcome Outcome, string? Detail = null)
{
    public bool IsUsable => Text is not null;
}

/// <summary>
/// Reads a data file without ever losing it silently.
///
/// The dangerous failure is not a missing file, it is a damaged one: the library loader used to
/// catch a parse error and carry on with an empty collection, so the next save wrote that empty
/// collection over the user's data. Here a damaged file is copied aside first, the .bak that
/// AtomicFile keeps is tried next, and the outcome says which of the two happened.
///
/// A file that cannot be read is reported as such and left completely alone: a lock or a
/// permission problem says nothing about the content, and silently substituting an older backup
/// would throw away newer data.
/// </summary>
public static class TextFileRecovery
{
    public const string DamagedSuffix = ".corrupt-";

    public static TextFileLoad Load(string path, Func<string, bool> isUsable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(isUsable);

        if (!File.Exists(path))
            return new TextFileLoad(null, TextFileOutcome.Missing);

        var text = TryRead(path);
        if (text is null)
            return new TextFileLoad(null, TextFileOutcome.ReadFailed, "the file could not be read");

        if (isUsable(text))
            return new TextFileLoad(text, TextFileOutcome.Loaded);

        // Damaged: keep a copy before anything can overwrite the original.
        var preserved = TryPreserve(path);

        var backupPath = path + AtomicFile.BackupSuffix;
        if (File.Exists(backupPath))
        {
            var backupText = TryRead(backupPath);
            if (backupText is not null && isUsable(backupText))
                return new TextFileLoad(backupText, TextFileOutcome.RestoredFromBackup, preserved);
        }

        return new TextFileLoad(null, TextFileOutcome.Damaged, preserved);
    }

    private static string? TryRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    private static string? TryPreserve(string path)
    {
        try
        {
            var target = path + DamagedSuffix + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(path, target, overwrite: true);
            return target;
        }
        catch
        {
            // Best effort: failing to copy must not stop the caller from reporting the damage.
            return null;
        }
    }
}
