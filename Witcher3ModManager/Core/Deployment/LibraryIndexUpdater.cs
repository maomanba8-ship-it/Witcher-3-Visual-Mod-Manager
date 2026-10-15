namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Builds a new index from an old one. Pure: it returns the result rather than mutating, so a
/// caller can decide whether to persist it and a failed operation never leaves a half-updated
/// index behind.
///
/// Replacing a package wholesale is deliberate. Merging target by target would let stale targets
/// survive an operation that no longer produces them, and a stale ownership claim is exactly what
/// makes a later rollback delete the wrong file.
/// </summary>
public static class LibraryIndexUpdater
{
    public static LibraryIndex WithPackage(
        LibraryIndex? index,
        string packageId,
        string displayName,
        IReadOnlyList<ComponentRecord>? components,
        string? operationId = null)
    {
        var current = index ?? LibraryIndex.Empty;
        if (string.IsNullOrWhiteSpace(packageId)) return current;

        var record = new PackageRecord(
            packageId,
            displayName ?? "",
            components ?? Array.Empty<ComponentRecord>(),
            operationId);

        var packages = current.Packages
            .Where(p => !string.Equals(p.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .Append(record)
            .OrderBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LibraryIndex(LibraryIndex.CurrentVersion, packages);
    }

    /// <summary>
    /// Replaces a package's declared components while keeping the targets that surviving
    /// components already had. An import replaces the storage copy; it does not undeploy, so
    /// clearing the targets would forget files that are still sitting in the game directory.
    /// A component recorded with its own targets (a deployment) always wins over the old ones.
    /// </summary>
    public static LibraryIndex WithDeclaredComponents(
        LibraryIndex? index,
        string packageId,
        string displayName,
        IReadOnlyList<ComponentRecord>? declared)
    {
        var previous = (index ?? LibraryIndex.Empty).FindPackage(packageId);
        var merged = (declared ?? Array.Empty<ComponentRecord>())
            .Select(component =>
            {
                if ((component.Targets?.Count ?? 0) > 0) return component;

                var old = previous?.Components?.FirstOrDefault(c =>
                    string.Equals(c.ComponentId, component.ComponentId, StringComparison.OrdinalIgnoreCase));

                return component with { Targets = old?.Targets ?? Array.Empty<TargetRecord>() };
            })
            .ToList();

        return WithPackage(index, packageId, displayName, merged);
    }

    /// <summary>
    /// Drops a package entirely. Used when its record is deleted: keeping its target claims would
    /// leave the index asserting ownership of files that no longer belong to anything.
    /// </summary>
    public static LibraryIndex WithoutPackage(LibraryIndex? index, string packageId)
    {
        var current = index ?? LibraryIndex.Empty;
        if (string.IsNullOrWhiteSpace(packageId)) return current;

        var packages = current.Packages
            .Where(p => !string.Equals(p.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return packages.Count == current.Packages.Count
            ? current
            : new LibraryIndex(LibraryIndex.CurrentVersion, packages);
    }
}
