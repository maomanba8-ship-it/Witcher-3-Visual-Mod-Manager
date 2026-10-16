using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Works out what deleting a package removes from the game directory. Each folder is planned as a
/// stash step rather than a plain recursive delete, so a failure part-way through puts the folders
/// back instead of leaving half a package behind.
///
/// Only the game directory is planned here: the storage copy sits in the manager's own area and is
/// the caller's business.
///
/// Whether a folder may be touched at all is not this planner's business either: shared folders are
/// filtered out by the caller before planning.
/// </summary>
public static class DeletePlanner
{
    public static DeploymentPlan Plan(
        string? gameModsRoot,
        string? gameDlcRoot,
        IEnumerable<string>? modFolders,
        IEnumerable<string>? dlcFolders,
        Func<string, bool>? pathExists = null)
    {
        var exists = pathExists ?? DefaultExists;

        var steps = new List<DeploymentStep>();
        steps.AddRange(PlanDomain(TargetDomain.Mods, gameModsRoot, modFolders, exists));
        steps.AddRange(PlanDomain(TargetDomain.Dlc, gameDlcRoot, dlcFolders, exists));

        return steps.Count == 0 ? DeploymentPlan.Empty : new DeploymentPlan(steps);
    }

    private static bool DefaultExists(string path) => Directory.Exists(path) || File.Exists(path);

    private static IEnumerable<DeploymentStep> PlanDomain(
        TargetDomain domain,
        string? gameRoot,
        IEnumerable<string>? folders,
        Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) yield break;

        foreach (var folder in Clean(folders))
        {
            // Both shapes have to go: the game only reads the enabled name, but a folder that is
            // currently disabled is still on disk and would otherwise be left behind. They are
            // separate steps with separate subjects so they cannot collide in the staging area.
            var anyFound = false;
            foreach (var name in new[] { folder, "~" + folder })
            {
                var path = Path.Combine(gameRoot, name);
                if (!exists(path)) continue;

                anyFound = true;
                yield return new DeploymentStep(domain, name, DeploymentAction.Stash, path, "");
            }

            // Say so when there is nothing anywhere for this folder, so a report can explain it.
            if (!anyFound)
                yield return new DeploymentStep(domain, folder, DeploymentAction.NothingToDo,
                    Path.Combine(gameRoot, folder), "");
        }
    }

    /// <summary>
    /// Records may carry a name that already has the disabled marker on it. The marker is stripped
    /// so the two on-disk shapes are computed once.
    /// </summary>
    private static IEnumerable<string> Clean(IEnumerable<string>? folders) =>
        (folders ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.StartsWith("~", StringComparison.Ordinal) ? f[1..] : f)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
}
