using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Shared;

/// <summary>
/// Pure shared-resource reference counter. Input: folder claims. Output: one
/// FolderReference per (domain, normalized folder), counting distinct owners. It never
/// moves/deletes/writes files, never calls UI/storage/service, and never special-cases a
/// specific mod name. The caller decides which claims to feed (for example, only enabled
/// components), which keeps enable/disable/delete logic fully derived from data.
/// </summary>
public static class SharedResourceResolver
{
    public static IReadOnlyList<FolderReference> Resolve(IEnumerable<FolderClaim> claims)
    {
        if (claims is null)
            return Array.Empty<FolderReference>();

        return claims
            .Select(c => new { c.ComponentId, c.Domain, Folder = NormalizeFolder(c.Folder) })
            .GroupBy(x => (x.Domain, x.Folder))
            .Select(g =>
            {
                var first = g.First();
                var owners = g
                    .Select(x => x.ComponentId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new FolderReference(first.Folder, first.Domain, owners.Count, owners);
            })
            .OrderBy(r => r.Domain)
            .ThenBy(r => r.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True if some owner other than <paramref name="excludeComponentId"/> references the
    /// given (domain, folder). Excluding by id lets Enable/Disable/Delete decide whether a
    /// folder is shared by another component without special-casing a mod name.
    /// </summary>
    public static bool IsSharedByOther(
        IEnumerable<FolderClaim> claims,
        TargetDomain domain,
        string folder,
        string? excludeComponentId = null)
    {
        var key = NormalizeFolder(folder);
        if (string.IsNullOrEmpty(key)) return false;

        var entry = Resolve(claims)
            .FirstOrDefault(r => r.Domain == domain &&
                                 string.Equals(r.Folder, key, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return false;

        return entry.Owners.Any(o =>
            string.IsNullOrEmpty(excludeComponentId) ||
            !string.Equals(o, excludeComponentId, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "";
        return folder.Replace('\\', '/').Trim('/').ToLowerInvariant();
    }
}