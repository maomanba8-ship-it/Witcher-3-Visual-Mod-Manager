using Witcher3ModManager.Core.Analysis;

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
    /// Clears what a package recorded for the game's bin directory, keeping the component itself.
    ///
    /// The index describes what is deployed now, so a bin claim has to go when the file is put
    /// back: leaving it behind makes a later delete report a file it "cannot restore" when in fact
    /// there is nothing of that package left in the game to restore. The component stays (with no
    /// targets) so the record still declares that it has bin files at all.
    /// </summary>
    public static LibraryIndex WithoutBinTargets(LibraryIndex? index, string packageId)
    {
        var current = index ?? LibraryIndex.Empty;
        var package = current.FindPackage(packageId);
        if (package is null) return current;

        var components = (package.Components ?? Array.Empty<ComponentRecord>())
            .Select(component => component.Domain == TargetDomain.Bin && (component.Targets?.Count ?? 0) > 0
                ? component with { Targets = Array.Empty<TargetRecord>() }
                : component)
            .ToList();

        return WithPackage(current, package.PackageId, package.DisplayName, components, package.LastOperationId);
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
