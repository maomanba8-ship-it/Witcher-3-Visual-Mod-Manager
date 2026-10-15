using System.IO;
using System.Text.Json;
using Witcher3ModManager.Core.Storage;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Persists the ownership index. Read the same way as the library itself: a damaged index falls
/// back to the previous version and is copied aside, and a file that cannot be read is left alone
/// rather than replaced.
///
/// The index is derived data, not the library: losing it degrades every answer to "who owns
/// this?" back to "unknown", which is the conservative direction. That is why a failed index
/// write must not fail the operation that triggered it.
/// </summary>
public sealed class LibraryIndexStore
{
    private readonly string _path;

    public LibraryIndexStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public string Path => _path;

    public TextFileOutcome LastLoadOutcome { get; private set; } = TextFileOutcome.Missing;

    public string? DamagedCopyPath { get; private set; }

    /// <summary>Set when the last save failed; the caller decides how to report it.</summary>
    public string? LastSaveError { get; private set; }

    public LibraryIndex Load()
    {
        var load = TextFileRecovery.Load(_path, LooksLikeAnIndex);
        LastLoadOutcome = load.Outcome;
        DamagedCopyPath = load.Detail;

        if (load.Text is null) return LibraryIndex.Empty;

        try
        {
            var index = JsonSerializer.Deserialize<LibraryIndex>(load.Text);
            return index is null || index.Version > LibraryIndex.CurrentVersion
                ? LibraryIndex.Empty
                : index;
        }
        catch
        {
            return LibraryIndex.Empty;
        }
    }

    public bool Save(LibraryIndex index)
    {
        try
        {
            var json = JsonSerializer.Serialize(index ?? LibraryIndex.Empty,
                new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(_path, json);
            LastSaveError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
            return false;
        }
    }

    private static bool LooksLikeAnIndex(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LibraryIndex>(json) is not null;
        }
        catch
        {
            return false;
        }
    }
}
