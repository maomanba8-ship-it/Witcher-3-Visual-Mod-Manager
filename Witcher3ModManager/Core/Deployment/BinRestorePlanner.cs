using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// What stopping a package does to its bin files: the steps to carry out, and the files it will
/// deliberately leave alone. Both halves matter — a file that cannot be put back has to be named,
/// not silently skipped.
/// </summary>
public sealed record BinRestorePlan(
    DeploymentPlan Steps,
    IReadOnlyList<OperationNote> Notes)
{
    public static BinRestorePlan Empty { get; } = new(DeploymentPlan.Empty, Array.Empty<OperationNote>());

    public bool IsEmpty => Steps.IsEmpty && Notes.Count == 0;
}

/// <summary>
/// Turns the ledger's decisions into work for the engine. Putting content back is a copy from the
/// backup store; taking away a file this manager created is a stash, so it goes through the same
/// "move aside first, undo on failure" path as everything else instead of being deleted outright.
/// </summary>
public static class BinRestorePlanner
{
    public static BinRestorePlan Plan(
        BinLedger? ledger,
        BinBackupStore? backups,
        string packageId,
        string? gameBinRoot,
        IEnumerable<string>? relativePaths,
        Func<string, string?> currentSha256,
        IEnumerable<string>? pathsOnRecord = null)
    {
        ArgumentNullException.ThrowIfNull(currentSha256);
        if (backups is null || string.IsNullOrWhiteSpace(gameBinRoot))
            return BinRestorePlan.Empty;

        var steps = new List<DeploymentStep>();
        var notes = new List<OperationNote>();

        foreach (var decision in BinLedger.PlanRestore(ledger, packageId, relativePaths, currentSha256, pathsOnRecord))
        {
            var gamePath = Path.Combine(gameBinRoot,
                decision.Path.Replace('/', Path.DirectorySeparatorChar));

            switch (decision.Action)
            {
                case BinRestoreAction.WriteBack when backups.Has(decision.Sha256):
                    steps.Add(new DeploymentStep(TargetDomain.Bin, decision.Path, DeploymentAction.Copy,
                        backups.PathFor(decision.Sha256!), gamePath));
                    break;

                case BinRestoreAction.WriteBack:
                    // The decision was to put something back and we cannot: that is worth reporting.
                    notes.Add(new OperationNote(OperationNoteKind.BinBackupMissing, TargetDomain.Bin, decision.Path));
                    break;

                case BinRestoreAction.Remove:
                    steps.Add(new DeploymentStep(TargetDomain.Bin, decision.Path, DeploymentAction.Stash,
                        gamePath, ""));
                    break;

                default:
                    // Some "leave it alone" answers are not news: a file we never wrote, or one
                    // that is already gone, is not something the user has to act on.
                    if (IsWorthReporting(decision.Reason))
                        notes.Add(new OperationNote(KindFor(decision.Reason), TargetDomain.Bin, decision.Path));
                    break;
            }
        }

        return new BinRestorePlan(
            steps.Count == 0 ? DeploymentPlan.Empty : new DeploymentPlan(steps),
            notes);
    }

    private static OperationNoteKind KindFor(BinRestoreReason reason) => reason switch
    {
        BinRestoreReason.ModifiedElsewhere => OperationNoteKind.BinModifiedElsewhere,
        BinRestoreReason.Missing => OperationNoteKind.BinAlreadyGone,
        _ => OperationNoteKind.BinNoRecord
    };

    private static bool IsWorthReporting(BinRestoreReason reason) =>
        reason is BinRestoreReason.NoRecord or BinRestoreReason.ModifiedElsewhere;
}
