using System.IO;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>Staging data left behind by one operation, with the size it occupies.</summary>
public sealed record StagingLeftover(string OperationId, string Path, long Bytes);

/// <summary>
/// Finds the staging folders that belong to operations the user has just handled.
///
/// It only reports. A staging folder is what an operation moved aside before it failed, and when a
/// rollback could not finish it may hold the only copy of something — so deleting it is the user's
/// decision, made with the paths and sizes in front of them. Nothing here deletes anything.
/// </summary>
public static class StagingReport
{
    public const string FolderName = "_w3mm_staging";

    public static IReadOnlyList<StagingLeftover> Find(
        string? gameRoot,
        IEnumerable<string>? operationIds,
        string stagingFolderName = FolderName)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) return Array.Empty<StagingLeftover>();

        var root = System.IO.Path.Combine(gameRoot, stagingFolderName);
        if (!Directory.Exists(root)) return Array.Empty<StagingLeftover>();

        var rootFull = System.IO.Path.GetFullPath(root).TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        var leftovers = new List<StagingLeftover>();
        foreach (var id in (operationIds ?? Array.Empty<string>())
                     .Where(i => !string.IsNullOrWhiteSpace(i))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // An operation id is a plain name. Anything that looks like a path is not one, and must
            // never be able to point outside the staging folder.
            if (id.IndexOfAny(new[] { '/', '\\' }) >= 0) continue;

            var path = System.IO.Path.Combine(root, id);

            // Only ever look inside our own staging folder, whatever the journal said.
            var pathFull = System.IO.Path.GetFullPath(path);
            if (!pathFull.StartsWith(rootFull + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Directory.Exists(path)) continue;

            leftovers.Add(new StagingLeftover(id, path, Measure(path)));
        }

        return leftovers;
    }

    private static long Measure(string path)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* a file that vanished while counting costs its size, nothing else */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
