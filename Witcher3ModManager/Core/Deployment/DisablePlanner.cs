using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Works out what disabling a package would do. Disabling uses the game's own convention: the
/// folder is renamed with a leading "~", which the game skips. That makes disabling reversible
/// without touching the content at all.
///
/// Shared folders are not this planner's business: whether a folder may be touched is decided by
/// the reference counts, so the caller filters them out before planning.
/// </summary>
public static class DisablePlanner
{
    public static DeploymentPlan Plan(
        string? gameModsRoot,
        string? gameDlcRoot,
        IEnumerable<string>? modFolders,
        IEnumerable<string>? dlcFolders,
        Func<string, bool>? directoryExists = null)
    {
        var exists = directoryExists ?? Directory.Exists;

        var steps = new List<DeploymentStep>();
        steps.AddRange(PlanDomain(TargetDomain.Mods, gameModsRoot, modFolders, exists));
        steps.AddRange(PlanDomain(TargetDomain.Dlc, gameDlcRoot, dlcFolders, exists));

        return steps.Count == 0 ? DeploymentPlan.Empty : new DeploymentPlan(steps);
    }

    private static IEnumerable<DeploymentStep> PlanDomain(
        TargetDomain domain,
        string? gameRoot,
        IEnumerable<string>? folders,
        Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) yield break;

        foreach (var folder in Clean(folders))
        {
            var enabled = Path.Combine(gameRoot, folder);
            var disabled = Path.Combine(gameRoot, "~" + folder);

            // A folder that is not there is already not enabled: nothing to do, but say so.
            yield return exists(enabled)
                ? new DeploymentStep(domain, folder, DeploymentAction.Move, enabled, disabled)
                : new DeploymentStep(domain, folder, DeploymentAction.NothingToDo, enabled, disabled);
        }
    }

    /// <summary>
    /// Records may carry a name that already has the disabled marker on it (a legacy shape the
    /// old helper also tolerated). The marker is stripped so the paths are computed once.
    /// </summary>
    private static IEnumerable<string> Clean(IEnumerable<string>? folders) =>
        (folders ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.StartsWith("~", StringComparison.Ordinal) ? f[1..] : f)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
}
