using System.IO;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Shared;

namespace Witcher3ModManager.Core.Manifest;

/// <summary>
/// Builds the conflict-analysis input (ComponentFile list) from each component's storage
/// folder. Pure and read-only: given a storage root and a set of folder claims, it uses
/// ManifestBuilder to record every file. It never moves/deletes/writes and never
/// special-cases a specific mod name. The caller decides which claims to include (for
/// example, only enabled components).
/// </summary>
public static class ManifestConflictInputProvider
{
    public static IReadOnlyList<ComponentFile> Build(
        string storageModsDir,
        IEnumerable<FolderClaim> claims)
    {
        if (string.IsNullOrWhiteSpace(storageModsDir) || claims is null)
            return Array.Empty<ComponentFile>();

        var files = new List<ComponentFile>();
        var claimedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Folder)) continue;
            var root = ResolveStorageFolder(storageModsDir, claim.Folder, claim.Domain);
            if (root is null) continue;

            // A single component may list both a mod and a dlc claim that collapse onto the
            // same physical storage folder (legacy records). Attribute it once so the same
            // files are not double-counted under two domains for one component.
            var dedupeKey = claim.ComponentId + "\u0001" + root;
            if (!claimedFolders.Add(dedupeKey)) continue;

            var manifest = ManifestBuilder.Build(root, claim.ComponentId, claim.Domain);
            files.AddRange(manifest.Files);
        }
        return files;
    }

    /// <summary>
    /// Builds the conflict input for components that carry their own absolute storage root
    /// instead of a folder name under the mods root (see <see cref="ComponentRootClaim"/>).
    /// Same rules as <see cref="Build"/>: read-only, structural, never a per-mod special case.
    /// A root that does not exist is skipped, so a stale record cannot abort the analysis.
    /// </summary>
    public static IReadOnlyList<ComponentFile> BuildFromRoots(IEnumerable<ComponentRootClaim> claims)
    {
        if (claims is null)
            return Array.Empty<ComponentFile>();

        var files = new List<ComponentFile>();
        var claimedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.RootPath)) continue;
            if (!Directory.Exists(claim.RootPath)) continue;

            var dedupeKey = claim.ComponentId + "\u0001" + claim.RootPath;
            if (!claimedRoots.Add(dedupeKey)) continue;

            var manifest = ManifestBuilder.Build(claim.RootPath, claim.ComponentId, claim.Domain);
            files.AddRange(manifest.Files);
        }
        return files;
    }

    /// <summary>
    /// Resolve a claimed component folder to a folder that actually exists under the
    /// storage root. The record stores the canonical deploy name (e.g. modFoo), but legacy
    /// storage may keep the raw package folder name (e.g. Foo). We try the exact name, then
    /// the deploy prefix stripped/added for the claim's domain. Pure name rule only - never a
    /// per-mod special case. Returns null when no variant exists.
    /// </summary>
    public static string? ResolveStorageFolder(string storageModsDir, string folder, TargetDomain domain)
    {
        var exact = Path.Combine(storageModsDir, folder);
        if (Directory.Exists(exact)) return exact;

        var prefix = domain == TargetDomain.Dlc ? "dlc" : "mod";

        if (folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var stripped = Path.Combine(storageModsDir, folder[prefix.Length..]);
            if (Directory.Exists(stripped)) return stripped;
        }

        if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var prefixed = Path.Combine(storageModsDir, prefix + folder);
            if (Directory.Exists(prefixed)) return prefixed;
        }

        return null;
    }
}
