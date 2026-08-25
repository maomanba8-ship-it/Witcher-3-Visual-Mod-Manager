using System;
using System.Collections.Generic;
using System.Linq;

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

    public static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "";
        return folder.Replace('\\', '/').Trim('/').ToLowerInvariant();
    }
}