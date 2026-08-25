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
        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Folder)) continue;
            var root = Path.Combine(storageModsDir, claim.Folder);
            if (!Directory.Exists(root)) continue;

            var manifest = ManifestBuilder.Build(root, claim.ComponentId, claim.Domain);
            files.AddRange(manifest.Files);
        }
        return files;
    }
}