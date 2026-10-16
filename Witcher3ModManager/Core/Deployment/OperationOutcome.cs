using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// What kind of fact a note states. The wording belongs to the interface, so a note carries the
/// kind and the data, never a finished sentence.
/// </summary>
public enum OperationNoteKind
{
    /// <summary>A step could not be carried out.</summary>
    StepFailed,

    /// <summary>A step that had run could not be undone after a later failure.</summary>
    RollbackProblem,

    /// <summary>A path could not be removed.</summary>
    PathNotRemoved,

    /// <summary>An enable found nothing to deploy from: absent from storage and from the game directory.</summary>
    NoSource,

    /// <summary>A bin file was left alone because what is on disk is not what we wrote.</summary>
    BinModifiedElsewhere,

    /// <summary>A bin file was left alone because there is no record of ever writing it.</summary>
    BinNoRecord,

    /// <summary>A bin file was left alone because it is not on disk at all.</summary>
    BinAlreadyGone,

    /// <summary>A bin file could not be put back because the content kept for it is gone.</summary>
    BinBackupMissing,

    /// <summary>A finished operation could not clear its staging area, so content is still on disk.</summary>
    StagingLeftBehind
}

/// <summary>
/// One fact worth reporting. Domain and Subject name the component where that makes sense; Detail
/// carries a path or a message from the layer that hit the problem.
/// </summary>
public sealed record OperationNote(
    OperationNoteKind Kind,
    TargetDomain? Domain = null,
    string? Subject = null,
    string? Detail = null);

/// <summary>
/// The result of one operation, in the shape the interface needs: whether it worked, what could not
/// be done, and whether the previous state was put back.
///
/// The service layer returns this instead of a bool so that a failure can name the step it died on,
/// rather than arriving as "it failed" with no way to find out where.
/// </summary>
public sealed record OperationOutcome
{
    public required OperationKind Kind { get; init; }
    public required bool Ok { get; init; }

    /// <summary>The engine's message, or an exception's. Set for a failure even when no step failed.</summary>
    public string? Failure { get; init; }

    /// <summary>
    /// True when the operation failed but everything it had already done was undone, so the state
    /// on disk is the one from before the operation.
    /// </summary>
    public bool RolledBack { get; init; }

    public IReadOnlyList<OperationNote> Notes { get; init; } = Array.Empty<OperationNote>();
    public IReadOnlyList<DeploymentStepResult> Steps { get; init; } = Array.Empty<DeploymentStepResult>();

    public bool HasNotes => Notes.Count > 0;

    /// <summary>Everything that went wrong, in the order it happened.</summary>
    public IReadOnlyList<OperationNote> Problems =>
        Notes.Where(n => n.Kind != OperationNoteKind.NoSource).ToList();

    /// <summary>The folders an enable found nothing to deploy from.</summary>
    public IReadOnlyList<OperationNote> WithoutSource =>
        Notes.Where(n => n.Kind == OperationNoteKind.NoSource).ToList();

    public static OperationOutcome Succeeded(
        OperationKind kind,
        IReadOnlyList<OperationNote>? notes = null,
        IReadOnlyList<DeploymentStepResult>? steps = null) =>
        new()
        {
            Kind = kind,
            Ok = true,
            Notes = notes ?? Array.Empty<OperationNote>(),
            Steps = steps ?? Array.Empty<DeploymentStepResult>()
        };

    /// <summary>
    /// A failure from outside the engine: an exception, a check the operation never got past, or a
    /// plain removal that did not work. The paths that could not be removed are kept as their own
    /// notes so the report can name them.
    /// </summary>
    public static OperationOutcome Failed(
        OperationKind kind,
        string failure,
        IEnumerable<string>? notRemoved = null) =>
        new()
        {
            Kind = kind,
            Ok = false,
            Failure = failure,
            Notes = (notRemoved ?? Array.Empty<string>())
                .Select(path => new OperationNote(OperationNoteKind.PathNotRemoved, Detail: path))
                .ToList()
        };

    /// <summary>
    /// Reads an engine result as an outcome, keeping the facts the caller knows on top of it (for an
    /// enable, the folders that had no source to deploy from).
    /// </summary>
    public static OperationOutcome From(
        OperationKind kind,
        DeploymentResult result,
        IReadOnlyList<OperationNote>? extraNotes = null)
    {
        var notes = new List<OperationNote>();

        foreach (var step in result.Steps.Where(s => s.Outcome == DeploymentStepOutcome.Failed))
            notes.Add(new OperationNote(OperationNoteKind.StepFailed, step.Domain, step.Subject,
                step.Detail ?? result.Failure));

        foreach (var problem in result.RollbackProblems)
            notes.Add(new OperationNote(OperationNoteKind.RollbackProblem, Detail: problem));

        if (!string.IsNullOrWhiteSpace(result.StagingLeftBehind))
            notes.Add(new OperationNote(OperationNoteKind.StagingLeftBehind,
                Detail: result.StagingLeftBehind));

        if (extraNotes is not null) notes.AddRange(extraNotes);

        return new OperationOutcome
        {
            Kind = kind,
            Ok = result.Ok,
            Failure = result.Failure,
            // A failure with no rollback problems means every step that had run was undone.
            RolledBack = !result.Ok && result.RollbackProblems.Count == 0,
            Notes = notes,
            Steps = result.Steps
        };
    }

    /// <summary>The folders an enable plan found nothing to deploy from, as notes.</summary>
    public static IReadOnlyList<OperationNote> NoSourceNotes(EnablePlan? plan) =>
        (plan?.WithoutSource ?? Array.Empty<EnablePlanStep>())
            .Select(step => new OperationNote(OperationNoteKind.NoSource, step.Domain, step.Folder))
            .ToList();

    /// <summary>
    /// The same thing for a plan of bin files: a file with no copy in the storage directory has
    /// nothing to deploy from either, and saying so is the difference between "enabled" meaning
    /// something and meaning nothing.
    /// </summary>
    public static IReadOnlyList<OperationNote> NoSourceNotes(DeploymentPlan? plan) =>
        (plan?.Skipped ?? Array.Empty<DeploymentStep>())
            .Select(step => new OperationNote(OperationNoteKind.NoSource, step.Domain, step.Subject))
            .ToList();
}
