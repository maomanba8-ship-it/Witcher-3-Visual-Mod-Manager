using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Works out what enabling a package would do, before anything is touched.
///
/// The decisions match what the manager has always done — restore the "~folder" the game uses as
/// a disabled marker, otherwise copy the storage copy in — but this time they are stated up front
/// as data. The executor then has no judgement to make, and the interesting part (which folders
/// are involved, which targets are already occupied) is testable without a file system, because
/// existence is asked through a predicate the caller supplies.
/// </summary>
public static class EnablePlanner
{
    public static EnablePlan Plan(
        string? gameModsRoot,
        string? gameDlcRoot,
        string? storageRoot,
        IEnumerable<string>? modFolders,
        IEnumerable<string>? dlcFolders,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? directoryIsEmpty = null)
    {
        var exists = directoryExists ?? Directory.Exists;
        var isEmpty = directoryIsEmpty ?? IsEmptyDirectory;

        var steps = new List<EnablePlanStep>();
        steps.AddRange(PlanDomain(TargetDomain.Mods, gameModsRoot, storageRoot, modFolders, exists, isEmpty));
        steps.AddRange(PlanDomain(TargetDomain.Dlc, gameDlcRoot, storageRoot, dlcFolders, exists, isEmpty));

        return steps.Count == 0
            ? EnablePlan.Empty
            : new EnablePlan(steps);
    }

    private static IEnumerable<EnablePlanStep> PlanDomain(
        TargetDomain domain,
        string? gameRoot,
        string? storageRoot,
        IEnumerable<string>? folders,
        Func<string, bool> exists,
        Func<string, bool> isEmpty)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) yield break;

        foreach (var folder in Distinct(folders))
        {
            var target = Path.Combine(gameRoot, folder);
            var disabled = Path.Combine(gameRoot, "~" + folder);
            var storageSource = string.IsNullOrWhiteSpace(storageRoot)
                ? null
                : Path.Combine(storageRoot, folder);

            var hasDisabled = exists(disabled);
            // An existing but empty folder is not "already deployed": it is what a failed or
            // interrupted operation leaves behind. Treating it as occupied silently marked the
            // package as enabled while nothing of it was on disk.
            var targetOccupied = exists(target) && !isEmpty(target);
            var hasStorage = storageSource is not null && exists(storageSource);

            var action =
                hasDisabled ? EnableAction.RestoreDisabledFolder :
                targetOccupied ? EnableAction.AlreadyInPlace :
                hasStorage ? EnableAction.CopyFromStorage :
                EnableAction.NoSource;

            yield return new EnablePlanStep(
                domain, folder, target, disabled,
                hasStorage ? storageSource : null,
                targetOccupied,
                action);
        }
    }

    private static bool IsEmptyDirectory(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return !entries.MoveNext();
        }
        catch
        {
            // A path that cannot be read is not evidence of emptiness; leave the old decision.
            return false;
        }
    }

    private static IEnumerable<string> Distinct(IEnumerable<string>? folders) =>
        (folders ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
}
