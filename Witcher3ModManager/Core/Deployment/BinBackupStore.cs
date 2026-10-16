using System.IO;
using System.Security.Cryptography;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Keeps copies of bin files as they were before this manager overwrote them, addressed by content
/// hash. The same bytes are stored once however many paths or packages refer to them, and a restore
/// only needs the hash, so nothing has to remember which mod a file came from.
///
/// Every write goes through a temporary file first: a half-written blob would be indistinguishable
/// from a real one, and would be put back into the game directory as if it were content.
/// </summary>
public sealed class BinBackupStore
{
    private readonly string _directory;

    public BinBackupStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>The folder holding the stored content. Named Root rather than Directory so it does
    /// not shadow the type of the same name inside this class.</summary>
    public string Root => _directory;

    /// <summary>Hash of a file, or null when it cannot be read (missing, locked, permissions).</summary>
    public static string? HashOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public bool Has(string? sha256) =>
        !string.IsNullOrWhiteSpace(sha256) && File.Exists(PathFor(sha256));

    /// <summary>
    /// Keeps a copy of the file and returns its hash. Returns null when the file could not be read
    /// or could not be kept: the caller must then treat the path as one it has no record for,
    /// rather than assume it can put anything back.
    /// </summary>
    public string? Capture(string? sourcePath)
    {
        var hash = HashOf(sourcePath);
        if (hash is null || sourcePath is null) return null;
        if (Has(hash)) return hash;

        try
        {
            Directory.CreateDirectory(_directory);
            var target = PathFor(hash);
            var temporary = target + ".tmp";
            File.Copy(sourcePath, temporary, overwrite: true);
            File.Move(temporary, target, overwrite: true);
            return hash;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Puts stored content back at a destination path, creating its folder as needed.</summary>
    public bool TryRestore(string? sha256, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sha256) || string.IsNullOrWhiteSpace(destinationPath)) return false;

        var source = PathFor(sha256);
        if (!File.Exists(source)) return false;

        try
        {
            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            var temporary = destinationPath + ".w3mm-tmp";
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destinationPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Where one piece of content lives. Public so a report can name it.</summary>
    public string PathFor(string sha256) => Path.Combine(_directory, sha256);
}
