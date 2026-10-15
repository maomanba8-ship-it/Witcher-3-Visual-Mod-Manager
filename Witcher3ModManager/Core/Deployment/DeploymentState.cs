namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// What the manager can say about a package's presence in the game directory.
/// </summary>
public enum DeploymentState
{
    /// <summary>Nothing recorded as deployed.</summary>
    NotDeployed,

    /// <summary>Every recorded target is present and still has the content we wrote.</summary>
    Deployed,

    /// <summary>Some targets are there, some are not.</summary>
    PartiallyDeployed,

    /// <summary>The situation does not add up and needs a person to look at it.</summary>
    NeedsRepair,

    /// <summary>The index has no record of this package, so ownership cannot be claimed.</summary>
    UnknownOwnership
}

/// <summary>
/// What was found at a recorded target.
/// </summary>
public enum TargetPresence
{
    Missing,
    Present,

    /// <summary>Present, but no longer carrying the content this manager wrote there.</summary>
    Modified
}

/// <summary>
/// Derives the deployment state from the index plus what is actually on disk.
///
/// The state is computed rather than stored: a stored flag would be one more thing that can
/// disagree with reality, and disagreeing with reality is the problem this is meant to remove.
/// The rules are deliberately blunt so that "I do not know" is a represented answer rather than
/// a guess: a package with no index entry is UnknownOwnership, and a deployed target whose
/// content changed is NeedsRepair, not silently assumed to be ours.
/// </summary>
public static class DeploymentStateEvaluator
{
    public static DeploymentState Evaluate(
        bool hasIndexRecord,
        bool isEnabled,
        IReadOnlyList<TargetRecord>? indexedTargets,
        Func<TargetRecord, TargetPresence>? presence)
    {
        // Without history nothing can be claimed, in either direction.
        if (!hasIndexRecord) return DeploymentState.UnknownOwnership;

        var targets = indexedTargets ?? Array.Empty<TargetRecord>();
        // Nothing recorded as deployed. That is only fine while the package is also not meant to
        // be deployed; an enabled package with no recorded target is a claim we cannot honour.
        if (targets.Count == 0)
            return isEnabled ? DeploymentState.NeedsRepair : DeploymentState.NotDeployed;

        if (presence is null) return DeploymentState.UnknownOwnership;

        var present = 0;
        var missing = 0;
        var modified = 0;

        foreach (var target in targets)
        {
            switch (presence(target))
            {
                case TargetPresence.Present: present++; break;
                case TargetPresence.Modified: modified++; break;
                default: missing++; break;
            }
        }

        // Something we wrote is no longer what we wrote: only a person can decide what that means.
        if (modified > 0) return DeploymentState.NeedsRepair;

        if (missing == 0) return DeploymentState.Deployed;

        if (present == 0)
        {
            // Enabled with nothing on disk is the classic mismatch: the record and the game
            // directory disagree and the user needs to be told, not guessed for.
            return isEnabled ? DeploymentState.NeedsRepair : DeploymentState.NotDeployed;
        }

        return DeploymentState.PartiallyDeployed;
    }
}
