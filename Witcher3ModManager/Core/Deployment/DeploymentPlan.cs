using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// What the engine should do about one location. The vocabulary is deliberately mechanical:
/// deciding *which* of these applies is the planner's job, and stating them separately keeps a
/// decision ("already in place") from looking like an instruction.
/// </summary>
public enum DeploymentAction
{
    /// <summary>Move the source onto the destination, which is a rename on one volume.</summary>
    Move,

    /// <summary>Copy the source into the destination; undoing it removes the copy.</summary>
    Copy,

    /// <summary>Move the source into this operation's staging area, leaving nothing behind.</summary>
    Stash,

    /// <summary>Nothing to do; recorded so a report can say why a folder was skipped.</summary>
    NothingToDo
}

/// <summary>
/// One mechanical step. DestinationPath is empty for a Stash step, because where the staging area
/// lives is the engine's business rather than the planner's.
/// </summary>
public sealed record DeploymentStep(
    TargetDomain Domain,
    string Subject,
    DeploymentAction Action,
    string SourcePath,
    string DestinationPath);

/// <summary>
/// The work an operation will perform, in order. Pure data: nothing here reads or changes a file.
/// </summary>
public sealed record DeploymentPlan(IReadOnlyList<DeploymentStep> Steps)
{
    public static DeploymentPlan Empty { get; } = new(Array.Empty<DeploymentStep>());

    public bool IsEmpty => Steps.Count == 0;

    /// <summary>Steps that will touch the file system.</summary>
    public IReadOnlyList<DeploymentStep> Actionable =>
        Steps.Where(s => s.Action != DeploymentAction.NothingToDo).ToList();

    /// <summary>Steps recorded only to explain a skip.</summary>
    public IReadOnlyList<DeploymentStep> Skipped =>
        Steps.Where(s => s.Action == DeploymentAction.NothingToDo).ToList();

    /// <summary>
    /// Several plans as one, in the order given. Enabling a package deploys its folders and its bin
    /// files in a single operation on purpose: one failure then rolls all of it back instead of
    /// leaving half a package behind.
    /// </summary>
    public static DeploymentPlan Combined(params DeploymentPlan?[] plans) =>
        new((plans ?? Array.Empty<DeploymentPlan>())
            .Where(p => p is not null)
            .SelectMany(p => p!.Steps)
            .ToList());
}
