using System.IO;
using System.Text;

namespace Witcher3ModManager.Core.Storage;

/// <summary>
/// Writes a text file so a reader can never observe a half-written version, and keeps the
/// previous contents beside it. The caller's file is the one the game reads, so an interrupted
/// save must not be able to leave it truncated; and because the previous content is the only
/// way back, it is kept as a .bak rather than discarded.
///
/// The write goes to a temporary file on the same volume, so the final swap is a rename rather
/// than a copy, and the temporary file never survives a failure.
/// </summary>
public static class AtomicFile
{
    public const string TempSuffix = ".tmp";
    public const string BackupSuffix = ".bak";

    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        Write(path, temp => File.WriteAllText(temp, contents, Utf8NoBom));
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(lines);

        var snapshot = lines.ToList();
        Write(path, temp => File.WriteAllLines(temp, snapshot, Utf8NoBom));
    }

    private static void Write(string path, Action<string> writeTemp)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + TempSuffix;
        try
        {
            writeTemp(temp);

            if (File.Exists(path))
                File.Replace(temp, path, path + BackupSuffix, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        catch
        {
            // Never leave a stray temporary file behind for the next run to trip over.
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
}
