using Witcher3ModManager.Core.Deployment;

namespace Witcher3ModManager.Core.Import;

/// <summary>One bin file this import would write that something else already deploys.</summary>
public sealed record BinCollision(string Path, IReadOnlyList<string> Owners);

/// <summary>
/// Finds bin files an import would deploy that another package already deploys.
///
/// Two packages writing the same file under the game's bin directory fight over it: the one
/// deployed later wins silently and the other one's settings stop applying. Storage cannot show
/// this — each package keeps its own copy in its own folder — so the overlap only exists in the
/// game directory, which is exactly where nobody looks before importing.
/// </summary>
public static class BinCollisionFinder
{
    public static IReadOnlyList<BinCollision> Find(
        IEnumerable<string>? plannedPaths,
        IEnumerable<(string Owner, IEnumerable<string>? Paths)>? deployedByOthers)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (owner, paths) in deployedByOthers ?? Array.Empty<(string, IEnumerable<string>?)>())
        {
            if (string.IsNullOrWhiteSpace(owner)) continue;

            foreach (var path in paths ?? Array.Empty<string>())
            {
                var key = BinLedger.Normalize(path);
                if (key.Length == 0) continue;

                if (!owners.TryGetValue(key, out var list)) owners[key] = list = new List<string>();
                if (!list.Contains(owner, StringComparer.OrdinalIgnoreCase)) list.Add(owner);
            }
        }

        var collisions = new List<BinCollision>();
        foreach (var planned in (plannedPaths ?? Array.Empty<string>())
                     .Select(BinLedger.Normalize)
                     .Where(p => p.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (owners.TryGetValue(planned, out var list) && list.Count > 0)
                collisions.Add(new BinCollision(planned, list));
        }

        return collisions;
    }
}
