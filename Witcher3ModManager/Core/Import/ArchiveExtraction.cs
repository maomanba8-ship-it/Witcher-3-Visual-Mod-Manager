namespace Witcher3ModManager.Core.Import;

/// <summary>
/// One entry as reported by the archive tool, before anything is written to disk.
/// </summary>
public sealed record ArchiveEntryInfo(string RelativePath, long Size, bool IsDirectory = false);

/// <summary>
/// Limits applied to an archive before extraction. The defaults are deliberately generous: a
/// large texture mod is legitimate, and this is a safety net against hostile or broken
/// archives, not a size policy for normal ones.
/// </summary>
public sealed record ArchiveExtractionLimits(
    int MaxEntries = 20000,
    long MaxTotalBytes = 8L * 1024 * 1024 * 1024,
    int MaxPathLength = 240)
{
    public static ArchiveExtractionLimits Default { get; } = new();
}

/// <summary>
/// Why an archive was refused. Kept as a closed set so callers and tests can assert on the
/// exact rule that fired instead of matching on message text.
/// </summary>
public enum ExtractionRejection
{
    None,
    EmptyEntryName,
    AbsolutePath,
    DriveQualified,
    InvalidEntryName,
    ParentTraversal,
    PathTooLong,
    TooManyEntries,
    ArchiveTooLarge
}

/// <summary>
/// The decision for one archive. Detail carries the offending entry so a report can name it.
/// </summary>
public sealed record ExtractionVerdict(bool IsSafe, ExtractionRejection Rejection, string? Detail = null)
{
    public static ExtractionVerdict Safe { get; } = new(true, ExtractionRejection.None);

    public static ExtractionVerdict Reject(ExtractionRejection rejection, string? detail = null) =>
        new(false, rejection, detail);
}
