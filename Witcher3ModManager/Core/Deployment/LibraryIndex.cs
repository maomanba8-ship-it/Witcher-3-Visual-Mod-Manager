using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Whether a recorded target is a folder or a single file.
/// </summary>
public enum TargetKind
{
    Folder,
    File
}

/// <summary>
/// A game-directory target this manager wrote, together with the content it wrote there.
/// WrittenHash is what makes "is this still ours?" answerable: without it, a target can only be
/// recognised by its path, and paths are exactly what a user or another tool may reuse.
/// </summary>
public sealed record TargetRecord(
    string RelativePath,
    TargetKind Kind,
    string WrittenHash,
    DateTime WrittenAt);

/// <summary>
/// One component of a package: which domain it belongs to, where its copy lives in the storage
/// directory, and which targets it put into the game directory.
/// </summary>
public sealed record ComponentRecord(
    string ComponentId,
    TargetDomain Domain,
    string StorageFolder,
    IReadOnlyList<TargetRecord> Targets)
{
    /// <summary>
    /// Stable, readable component identity derived from the package and the target it occupies.
    /// Deterministic on purpose: it can be recomputed from the records we already have, so it
    /// needs no migration and no new field in the existing library file.
    /// </summary>
    public static string MakeId(string packageId, TargetDomain domain, string folder) =>
        $"{packageId}:{domain}:{Core.Shared.SharedResourceResolver.NormalizeFolder(folder)}";
}

/// <summary>
/// One package as the index knows it. PackageId is the existing record id: the index describes
/// the library that is already there instead of introducing a second identity to migrate.
/// </summary>
public sealed record PackageRecord(
    string PackageId,
    string DisplayName,
    IReadOnlyList<ComponentRecord> Components,
    string? LastOperationId = null);

/// <summary>
/// The ownership index: which package put which target where, and with what content. It is the
/// answer to "is this file ours?" and the basis for a rollback that does not touch anything else.
///
/// Pure and read-only. Nothing here reads or writes a file, so a malformed index can never be
/// mistaken for a damaged library, and every rule below is testable without an installation.
/// </summary>
public sealed record LibraryIndex(
    int Version,
    IReadOnlyList<PackageRecord> Packages)
{
    public const int CurrentVersion = 1;

    public static LibraryIndex Empty { get; } = new(CurrentVersion, Array.Empty<PackageRecord>());

    public PackageRecord? FindPackage(string? packageId) =>
        string.IsNullOrWhiteSpace(packageId)
            ? null
            : Packages.FirstOrDefault(p => string.Equals(p.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

    public ComponentRecord? FindComponent(string? componentId) =>
        string.IsNullOrWhiteSpace(componentId)
            ? null
            : Packages
                .SelectMany(p => p.Components ?? Array.Empty<ComponentRecord>())
                .FirstOrDefault(c => string.Equals(c.ComponentId, componentId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Which component claims a game-directory path, or null when nobody does. Comparison ignores
    /// separators and case, because that is how the file system treats these paths.
    /// </summary>
    public ComponentRecord? OwnerOf(string? relativePath)
    {
        var key = NormalizeTarget(relativePath);
        if (key.Length == 0) return null;

        return Packages
            .SelectMany(p => p.Components ?? Array.Empty<ComponentRecord>())
            .FirstOrDefault(c => (c.Targets ?? Array.Empty<TargetRecord>())
                .Any(t => string.Equals(NormalizeTarget(t.RelativePath), key, StringComparison.Ordinal)));
    }

    public static string NormalizeTarget(string? relativePath) =>
        (relativePath ?? "").Replace('\\', '/').Trim('/').ToLowerInvariant();
}
