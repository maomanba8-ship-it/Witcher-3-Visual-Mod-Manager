using System.IO;
using System.Text.Json;
using Witcher3ModManager.Core.Storage;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Keeps one file per running operation under a journal directory, deleting it when the operation
/// finishes. Whatever is still there at startup is an operation that did not finish.
///
/// The store never decides anything: it writes, deletes and lists. Recovering from a leftover
/// journal is a user decision, so nothing here rolls back or retries on its own.
/// </summary>
public sealed class OperationJournalStore
{
    /// <summary>Where journals go once a person has looked at them.</summary>
    public const string HandledFolderName = "handled";

    private readonly string _directory;

    public OperationJournalStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string Directory => _directory;

    /// <summary>Journals that have been looked at live here, out of the way of the startup check.</summary>
    public string HandledDirectory => Path.Combine(_directory, HandledFolderName);

    /// <summary>
    /// Moves leftover journals aside, at the user's request, so the startup report stops asking
    /// about them. The records are kept rather than deleted: the point is to stop the prompting,
    /// not to throw away the only evidence of what happened. Nothing calls this on its own.
    /// </summary>
    public int Handle(IEnumerable<string>? operationIds)
    {
        var handled = HandledDirectory;
        var count = 0;

        foreach (var id in (operationIds ?? Array.Empty<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct())
        {
            var path = PathFor(id);
            if (path is null || !File.Exists(path)) continue;

            try
            {
                System.IO.Directory.CreateDirectory(handled);
                File.Move(path, Path.Combine(handled, Path.GetFileName(path)), overwrite: true);

                var backup = path + AtomicFile.BackupSuffix;
                if (File.Exists(backup))
                    File.Move(backup, Path.Combine(handled, Path.GetFileName(backup)), overwrite: true);

                count++;
            }
            catch
            {
                // A journal that cannot be moved is simply reported again next time.
            }
        }

        return count;
    }

    /// <summary>
    /// Writes the journal and returns it. Returns null when it could not be written: an unwritable
    /// journal is worth reporting, but it must not stop the operation the user asked for.
    /// </summary>
    public OperationJournal? Begin(OperationKind kind, string subject, IReadOnlyList<string>? targets)
    {
        var journal = new OperationJournal(
            Guid.NewGuid().ToString("N")[..12],
            kind,
            DateTime.Now,
            subject ?? "",
            targets ?? Array.Empty<string>(),
            Completed: false);

        return TryWrite(journal) ? journal : null;
    }

    /// <summary>Deletes the journal: the operation finished, whatever its outcome.</summary>
    public void Complete(string? operationId)
    {
        var path = PathFor(operationId);
        if (path is null) return;

        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* leaving a journal behind is reported at startup, which is safe */ }
    }

    /// <summary>
    /// Keeps the journal and records why the operation failed. This is the file the startup check
    /// will find.
    /// </summary>
    public void Fail(OperationJournal? journal, string? failure)
    {
        if (journal is null) return;
        TryWrite(journal with { Completed = false, Failure = failure });
    }

    /// <summary>
    /// Operations that never finished. A journal that cannot be parsed is still reported, as an
    /// entry with no detail, because its presence means something was interrupted.
    /// </summary>
    public IReadOnlyList<OperationJournal> Leftovers()
    {
        if (!System.IO.Directory.Exists(_directory)) return Array.Empty<OperationJournal>();

        var leftovers = new List<OperationJournal>();
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var journal = JsonSerializer.Deserialize<OperationJournal>(File.ReadAllText(file));
                if (journal is not null) leftovers.Add(journal);
            }
            catch
            {
                leftovers.Add(new OperationJournal(
                    Path.GetFileNameWithoutExtension(file),
                    OperationKind.Enable,
                    DateTime.MinValue,
                    Path.GetFileNameWithoutExtension(file),
                    Array.Empty<string>(),
                    Completed: false,
                    Failure: "unreadable journal"));
            }
        }

        return leftovers
            .OrderBy(j => j.StartedAt)
            .ToList();
    }

    private bool TryWrite(OperationJournal journal)
    {
        try
        {
            var path = PathFor(journal.OperationId)!;
            var json = JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string? PathFor(string? operationId) =>
        string.IsNullOrWhiteSpace(operationId)
            ? null
            : Path.Combine(_directory, operationId + ".json");
}
