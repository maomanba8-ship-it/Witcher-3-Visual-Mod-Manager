using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// What enabling one component folder needs to do.
/// </summary>
public enum EnableAction
{
    /// <summary>The game holds a "~folder": move it back, backing up whatever occupies the name.</summary>
    RestoreDisabledFolder,

    /// <summary>Nothing is in the game directory and the storage copy is: copy it in.</summary>
    CopyFromStorage,

    /// <summary>Already where it belongs; nothing to do.</summary>
    AlreadyInPlace,

    /// <summary>Neither the game directory nor storage has anything for this folder.</summary>
    NoSource
}

/// <summary>
/// One planned step, with the paths it concerns. Paths are resolved up front so the executor and
/// the journal agree on exactly which locations an operation was about to touch.
/// </summary>
public sealed record EnablePlanStep(
    TargetDomain Domain,
    string Folder,
    string TargetPath,
    string DisabledPath,
    string? StorageSourcePath,
    bool TargetOccupied,
    EnableAction Action);

/// <summary>
/// The whole plan for one enable. Pure data: producing it reads nothing and changes nothing.
/// </summary>
public sealed record EnablePlan(IReadOnlyList<EnablePlanStep> Steps)
{
    public static EnablePlan Empty { get; } = new(Array.Empty<EnablePlanStep>());

    /// <summary>Steps that will actually touch the file system.</summary>
    public IReadOnlyList<EnablePlanStep> Work =>
        Steps.Where(s => s.Action is EnableAction.RestoreDisabledFolder or EnableAction.CopyFromStorage)
            .ToList();

    /// <summary>Steps that need a person to look at them (nothing to enable from).</summary>
    public IReadOnlyList<EnablePlanStep> WithoutSource =>
        Steps.Where(s => s.Action == EnableAction.NoSource).ToList();

    /// <summary>True when a step would replace something already sitting at the target name.</summary>
    public bool ReplacesOccupiedTarget =>
        Work.Any(s => s.Action == EnableAction.RestoreDisabledFolder && s.TargetOccupied);

    /// <summary>
    /// Translates the decisions into mechanical steps. The planner says which case each folder is
    /// in; the engine only needs to know what to do with a source and a destination.
    /// </summary>
    public DeploymentPlan ToDeploymentPlan() =>
        new(Steps.Select(step => new DeploymentStep(
                step.Domain,
                step.Folder,
                step.Action switch
                {
                    EnableAction.RestoreDisabledFolder => DeploymentAction.Move,
                    EnableAction.CopyFromStorage => DeploymentAction.Copy,
                    _ => DeploymentAction.NothingToDo
                },
                step.Action == EnableAction.RestoreDisabledFolder ? step.DisabledPath : step.StorageSourcePath ?? "",
                step.TargetPath))
            .ToList());
}
