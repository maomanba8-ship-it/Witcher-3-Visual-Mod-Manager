namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Decides what the bin ledger should remember as "the content before this write".
///
/// A file that already holds exactly what this package is about to write was not put there by the
/// game — it is this package, installed by hand (or by an earlier run). Remembering it as the
/// original would make a later disable restore a copy of the mod and leave it active. Treating it
/// as "nothing was there" is what makes disable remove it.
///
/// A different file is genuine previous content (often a game file or another mod's version) and
/// is remembered so disable can put it back. An unknown incoming hash never counts as identical:
/// "cannot tell" must not turn into a delete.
/// </summary>
public static class BinTakeover
{
    public static string? OriginalToRecord(string? existingHash, string? incomingHash)
    {
        if (string.IsNullOrEmpty(existingHash)) return null;

        if (!string.IsNullOrEmpty(incomingHash)
            && string.Equals(existingHash, incomingHash, StringComparison.OrdinalIgnoreCase))
            return null;

        return existingHash;
    }
}
