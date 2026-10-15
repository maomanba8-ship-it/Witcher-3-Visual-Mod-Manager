namespace Witcher3ModManager.Core.Import;

/// <summary>
/// Pure decision layer for archive extraction: given the entries an archive claims to contain,
/// decide whether unpacking them can be done safely, or refuse with the rule that fired.
///
/// It never touches the file system and never runs a tool, so every rule here is testable with
/// a hand-written entry list. Two classes of danger are covered: entries that would land
/// outside the destination directory, and archives that are too large or too numerous to
/// unpack (the zip-bomb shape).
/// </summary>
public static class ArchiveExtractionPolicy
{
    public static ExtractionVerdict Evaluate(
        IEnumerable<ArchiveEntryInfo>? entries,
        ArchiveExtractionLimits? limits = null)
    {
        if (entries is null) return ExtractionVerdict.Safe;
        limits ??= ArchiveExtractionLimits.Default;

        var fileCount = 0;
        long totalBytes = 0;

        foreach (var entry in entries)
        {
            var path = entry.RelativePath ?? "";

            var unsafeVerdict = CheckPath(path, limits);
            if (unsafeVerdict is not null) return unsafeVerdict;

            if (entry.IsDirectory) continue;

            fileCount++;
            if (fileCount > limits.MaxEntries)
                return ExtractionVerdict.Reject(ExtractionRejection.TooManyEntries,
                    $"{fileCount} files exceeds the limit of {limits.MaxEntries}");

            // Guard the sum itself: a hostile archive can claim sizes that overflow a long.
            totalBytes = entry.Size > long.MaxValue - totalBytes ? long.MaxValue : totalBytes + entry.Size;
            if (totalBytes > limits.MaxTotalBytes)
                return ExtractionVerdict.Reject(ExtractionRejection.ArchiveTooLarge,
                    $"{totalBytes} bytes exceeds the limit of {limits.MaxTotalBytes}");
        }

        return ExtractionVerdict.Safe;
    }

    private static ExtractionVerdict? CheckPath(string path, ArchiveExtractionLimits limits)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ExtractionVerdict.Reject(ExtractionRejection.EmptyEntryName, path);

        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            return ExtractionVerdict.Reject(ExtractionRejection.DriveQualified, path);

        if (path.StartsWith('/') || path.StartsWith('\\'))
            return ExtractionVerdict.Reject(ExtractionRejection.AbsolutePath, path);

        // A remaining colon turns the name into an NTFS alternate data stream, which is another
        // way out of a plain file name.
        if (path.Contains(':'))
            return ExtractionVerdict.Reject(ExtractionRejection.InvalidEntryName, path);

        // Archives written on any platform may use either separator; treat both the same way.
        var normalized = path.Replace('\\', '/');
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
                return ExtractionVerdict.Reject(ExtractionRejection.ParentTraversal, path);
        }

        if (path.Length > limits.MaxPathLength)
            return ExtractionVerdict.Reject(ExtractionRejection.PathTooLong,
                $"{path.Length} characters exceeds the limit of {limits.MaxPathLength}");

        return null;
    }
}
