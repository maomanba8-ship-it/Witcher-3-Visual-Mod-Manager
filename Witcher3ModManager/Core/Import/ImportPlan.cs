using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Import;

/// <summary>
/// What an import is going to do to one component.
/// </summary>
public enum ImportAction
{
    /// <summary>Nothing with this target exists yet.</summary>
    Create,

    /// <summary>A component with this target name already exists and would be overwritten.</summary>
    Update,

    /// <summary>Not a deployable component, so it needs a user decision rather than a target.</summary>
    Unsupported
}

/// <summary>
/// One component as the import would place it. DeployName is the name that will actually be
/// written, which is not always the package's own folder name.
/// </summary>
public sealed record ImportPlanComponent(
    string Name,
    ComponentKind Kind,
    string RootPath,
    string DeployName,
    ImportAction Action);

/// <summary>
/// The read-only description of an import, built before anything is written. It exists so the
/// UI can show what would happen, and so "cancel" can be verified to leave the storage, the
/// game directory and the records untouched: nothing in this model performs work by itself.
///
/// RequiresConfirmation is true whenever the plan would overwrite something or the analyzer was
/// not certain, which is the set of cases where a second click is warranted.
/// </summary>
public sealed record ImportPlan(
    IReadOnlyList<ImportPlanComponent> Components,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Decisions,
    bool RequiresConfirmation,
    string? RejectReason)
{
    public bool IsRejected => RejectReason is not null;

    public bool IsEmpty => Components.Count == 0;

    /// <summary>The components that would replace existing ones.</summary>
    public IReadOnlyList<ImportPlanComponent> Overwrites =>
        Components.Where(c => c.Action == ImportAction.Update).ToList();
}
