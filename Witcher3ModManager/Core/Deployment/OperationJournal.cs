namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// The kind of operation a journal describes.
/// </summary>
public enum OperationKind
{
    Import,
    Enable,
    Disable,
    Delete,
    ApplyPreset
}

/// <summary>
/// The record of one operation, written before it touches anything and kept only if it does not
/// finish. It answers the question the manager could not answer before: "was something left
/// half-done last time, and what was it about?"
///
/// Targets are the paths the operation intended to touch. They are recorded up front on purpose:
/// after a failure, the paths that were *planned* are the ones worth looking at, and a list
/// assembled afterwards would only describe whatever the failure left behind.
/// </summary>
public sealed record OperationJournal(
    string OperationId,
    OperationKind Kind,
    DateTime StartedAt,
    string Subject,
    IReadOnlyList<string> Targets,
    bool Completed,
    string? Failure = null);
