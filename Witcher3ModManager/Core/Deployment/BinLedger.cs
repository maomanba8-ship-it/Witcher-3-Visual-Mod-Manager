namespace Witcher3ModManager.Core.Deployment;

/// <summary>One package that wrote one bin path, and the content it wrote.</summary>
public sealed record BinWriter(string PackageId, string Sha256, DateTime At);

/// <summary>
/// What the ledger knows about one path under the game's bin directory.
///
/// Original is what was there before this manager first touched the path: a hash, or null when
/// there was nothing. Writers are the packages that have written since, oldest first, so the last
/// one is what is in effect. That order is the whole reason this is a separate file: the ownership
/// index is rewritten per package and cannot say who wrote *after* whom.
/// </summary>
public sealed record BinPathRecord(
    string Path,
    string? Original,
    IReadOnlyList<BinWriter> Writers);

/// <summary>What should happen to one bin path now that a package stops using it.</summary>
public enum BinRestoreAction
{
    /// <summary>Put recorded content back.</summary>
    WriteBack,

    /// <summary>Remove the file: nothing was there before we created it.</summary>
    Remove,

    /// <summary>Do nothing, and say why.</summary>
    LeaveAlone
}

/// <summary>Why a restore step says what it says. The wording belongs to the interface.</summary>
public enum BinRestoreReason
{
    /// <summary>The ledger has no claim on this path for this package (an old entry, or never recorded).</summary>
    NoRecord,

    /// <summary>What is on disk is not what we wrote: someone else changed it.</summary>
    ModifiedElsewhere,

    /// <summary>The file is not on disk at all; leaving it that way is the honest answer.</summary>
    Missing,

    /// <summary>
    /// Nothing ever wrote this path on this package's behalf: the file, if it exists, has nothing
    /// to do with it. Not worth telling the user about.
    /// </summary>
    NotOurs,

    /// <summary>Another package wrote after us; the content to put back is that one's.</summary>
    Superseded,

    /// <summary>Nobody else claims it; put back what was there before we first wrote.</summary>
    BackToOriginal,

    /// <summary>Nobody else claims it and it did not exist before we wrote it.</summary>
    CreatedByUs
}

/// <summary>One decision about one path. Data only; carrying it out is the caller's business.</summary>
public sealed record BinRestoreStep(
    string Path,
    BinRestoreAction Action,
    string? Sha256,
    BinRestoreReason Reason);

/// <summary>
/// The record of what the manager did to the game's bin directory, and what was there first. It
/// exists to make bin deployment reversible: without the original content there is nothing to put
/// back, and without the write order there is no way to tell which of two mods sharing one file is
/// the one whose content should disappear.
///
/// Pure: nothing here reads or writes a file, so every rule is testable without an installation.
/// </summary>
public sealed record BinLedger
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyDictionary<string, BinPathRecord> Paths { get; init; } =
        new Dictionary<string, BinPathRecord>(StringComparer.OrdinalIgnoreCase);

    public static BinLedger Empty { get; } = new();

    public int Count => Paths.Count;

    public BinPathRecord? Find(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null
        : Paths.TryGetValue(Normalize(path), out var record) ? record : null;

    /// <summary>
    /// Records that a package has just written this content. ShaBeforeWrite is what the file held
    /// before the write (null when there was nothing); it is kept only the first time a path is
    /// seen, because that is the state the path has to be able to go back to.
    ///
    /// A package has one claim per path, not a count: writing again replaces its previous entry
    /// and moves it to the end, which is what "who wrote last" means.
    /// </summary>
    public BinLedger WithWrite(
        string path,
        string packageId,
        string writtenSha256,
        string? shaBeforeWrite,
        DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var key = Normalize(path);
        var existing = Find(key);

        var writers = (existing?.Writers ?? Array.Empty<BinWriter>())
            .Where(w => !IsWrittenBy(w, packageId))
            .Append(new BinWriter(packageId, writtenSha256, at))
            .ToList();

        return new BinLedger
        {
            Version = Version,
            Paths = Replace(Paths, key,
                new BinPathRecord(key, existing is null ? shaBeforeWrite : existing.Original, writers))
        };
    }

    /// <summary>
    /// Drops a package's claim on a path. When no claim is left the path is forgotten entirely:
    /// the file has been put back to what was there before, so there is nothing left to remember,
    /// and the next write will capture the original again.
    /// </summary>
    public BinLedger WithoutWriter(string path, string packageId)
    {
        var record = Find(path);
        if (record is null) return this;

        var writers = record.Writers.Where(w => !IsWrittenBy(w, packageId)).ToList();
        var key = record.Path;

        if (writers.Count == 0)
        {
            var without = new Dictionary<string, BinPathRecord>(Paths, StringComparer.OrdinalIgnoreCase);
            without.Remove(key);
            return this with { Paths = without };
        }

        return this with
        {
            Paths = Replace(Paths, key, record with { Writers = writers })
        };
    }

    /// <summary>
    /// What should happen to each of a package's bin paths now that it stops using them. The
    /// current content is asked for through currentSha256 (null = not on disk), so the decisions
    /// are testable without a file system.
    ///
    /// Nothing is ever guessed: no record, unexpected content, or a file that is already gone all
    /// end as "leave it alone" with a reason, and the caller reports it.
    /// </summary>
    public static IReadOnlyList<BinRestoreStep> PlanRestore(
        BinLedger? ledger,
        string packageId,
        IEnumerable<string>? paths,
        Func<string, string?> currentSha256,
        IEnumerable<string>? pathsOnRecord = null)
    {
        ArgumentNullException.ThrowIfNull(currentSha256);

        var onRecord = new HashSet<string>(
            NormalizeAll(pathsOnRecord), StringComparer.OrdinalIgnoreCase);

        var steps = new List<BinRestoreStep>();
        foreach (var path in NormalizeAll(paths))
            steps.Add(PlanOne(ledger, packageId, path, currentSha256, onRecord));

        return steps;
    }

    private static BinRestoreStep PlanOne(
        BinLedger? ledger,
        string packageId,
        string path,
        Func<string, string?> currentSha256,
        IReadOnlySet<string> pathsOnRecord)
    {
        var record = ledger?.Find(path);
        var writers = record?.Writers ?? Array.Empty<BinWriter>();
        var mine = writers
            .Where(w => IsWrittenBy(w, packageId))
            .ToList();

        if (mine.Count == 0)
        {
            // The ledger says nothing about this package writing the file. If another record does
            // — the ownership index, which remembers writes from before the ledger existed — then
            // this package did write it once and it cannot be put back. Otherwise the file is none
            // of this package's business and there is nothing to report.
            return Leave(path, pathsOnRecord.Contains(path)
                ? BinRestoreReason.NoRecord
                : BinRestoreReason.NotOurs);
        }

        var current = currentSha256(path);
        if (current is null) return Leave(path, BinRestoreReason.Missing);
        if (!string.Equals(current, mine[^1].Sha256, StringComparison.OrdinalIgnoreCase))
            return Leave(path, BinRestoreReason.ModifiedElsewhere);

        var others = writers.Where(w => !IsWrittenBy(w, packageId)).ToList();
        if (others.Count > 0)
            return new BinRestoreStep(path, BinRestoreAction.WriteBack, others[^1].Sha256,
                BinRestoreReason.Superseded);

        // Having a claim of our own means the ledger knows this path, so record is not null here.
        return record!.Original is null
            ? new BinRestoreStep(path, BinRestoreAction.Remove, null, BinRestoreReason.CreatedByUs)
            : new BinRestoreStep(path, BinRestoreAction.WriteBack, record.Original,
                BinRestoreReason.BackToOriginal);
    }

    private static BinRestoreStep Leave(string path, BinRestoreReason reason) =>
        new(path, BinRestoreAction.LeaveAlone, null, reason);

    private static bool IsWrittenBy(BinWriter writer, string packageId) =>
        string.Equals(writer.PackageId, packageId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Paths are compared the way Windows compares them: separators and case do not matter.</summary>
    public static string Normalize(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('/');

    private static IEnumerable<string> NormalizeAll(IEnumerable<string>? paths) =>
        (paths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, BinPathRecord> Replace(
        IReadOnlyDictionary<string, BinPathRecord> paths,
        string key,
        BinPathRecord record)
    {
        var updated = new Dictionary<string, BinPathRecord>(paths, StringComparer.OrdinalIgnoreCase)
        {
            [key] = record
        };
        return updated;
    }
}
