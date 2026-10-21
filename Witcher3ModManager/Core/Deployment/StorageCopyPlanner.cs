using System.IO;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>One folder that is in the game directory but has no copy in the storage directory.</summary>
public sealed record StorageCopyItem(string Folder, string SourcePath, string TargetPath);

/// <summary>
/// Works out which folders a package would need copied into storage.
///
/// A package that was adopted or installed by an earlier build can live only in the game
/// directory. That works for enabling and disabling, but if the game copy is ever lost there is
/// nothing left to deploy from — so the copy is worth making, on request.
///
/// Nothing is decided here about the game directory: the copy only ever reads it.
/// </summary>
public static class StorageCopyPlanner
{
    public static IReadOnlyList<StorageCopyItem> Plan(
        IEnumerable<string>? folders,
        string? gameRoot,
        string? storageRoot,
        Func<string, bool>? directoryExists = null)
    {
        var exists = directoryExists ?? Directory.Exists;
        if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(storageRoot))
            return Array.Empty<StorageCopyItem>();

        var items = new List<StorageCopyItem>();

        foreach (var folder in Clean(folders))
        {
            var target = Path.Combine(storageRoot!, folder);
            // Already stored: there is nothing to gain from a second copy.
            if (exists(target)) continue;

            var enabled = Path.Combine(gameRoot!, folder);
            var disabled = Path.Combine(gameRoot!, "~" + folder);

            // "~name" is the same content with the mod turned off, so it is a valid source.
            var source = exists(enabled) ? enabled : exists(disabled) ? disabled : null;
            if (source is null) continue;

            // Copying a folder onto itself would be a very expensive no-op.
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            items.Add(new StorageCopyItem(folder, source, target));
        }

        return items;
    }

    private static IEnumerable<string> Clean(IEnumerable<string>? folders) =>
        (folders ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.StartsWith("~", StringComparison.Ordinal) ? f[1..] : f)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
}
