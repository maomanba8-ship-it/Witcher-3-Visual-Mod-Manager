using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>How something the index recorded no longer matches what is on disk.</summary>
public enum DriftKind
{
    /// <summary>Nothing is at the path any more.</summary>
    Missing,

    /// <summary>Something is there, but not the content this manager wrote.</summary>
    Modified
}

/// <summary>One deployed target that no longer matches the index, named so a report can point at it.</summary>
public sealed record TargetDrift(
    string PackageId,
    string DisplayName,
    TargetDomain Domain,
    string RelativePath,
    DriftKind Kind);

/// <summary>One recorded target, together with what the index says about the component holding it.</summary>
public sealed record DeployedTarget(
    string PackageId,
    string DisplayName,
    TargetDomain Domain,
    string StorageFolder,
    TargetRecord Target);

/// <summary>
/// Compares what the index says was deployed with what is actually there.
///
/// Read-only by construction: the disk is the truth, and the only useful thing to do about a
/// difference is to say whose content the user has. Silently "repairing" it would either throw away
/// someone else's edit or pretend our own record is right, and both are worse than saying it.
///
/// What counts as present is asked through a delegate, so the rules can be exercised without an
/// installation — and so that "a folder renamed to its disabled form" can be treated as present by
/// the caller, which is exactly what this manager's own disable does.
/// </summary>
public static class DeploymentDrift
{
    public static IReadOnlyList<TargetDrift> Find(
        LibraryIndex? index,
        Func<DeployedTarget, TargetPresence>? presence)
    {
        if (index is null || presence is null) return Array.Empty<TargetDrift>();

        var drifts = new List<TargetDrift>();
        foreach (var package in index.Packages ?? Array.Empty<PackageRecord>())
        {
            foreach (var component in package.Components ?? Array.Empty<ComponentRecord>())
            {
                foreach (var target in component.Targets ?? Array.Empty<TargetRecord>())
                {
                    var state = presence(new DeployedTarget(
                        package.PackageId, package.DisplayName, component.Domain,
                        component.StorageFolder, target));

                    if (state == TargetPresence.Present) continue;

                    drifts.Add(new TargetDrift(
                        package.PackageId,
                        package.DisplayName,
                        component.Domain,
                        target.RelativePath,
                        state == TargetPresence.Missing ? DriftKind.Missing : DriftKind.Modified));
                }
            }
        }

        return drifts;
    }
}
