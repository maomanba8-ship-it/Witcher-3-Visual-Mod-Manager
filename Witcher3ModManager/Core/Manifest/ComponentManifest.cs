using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Manifest;

/// <summary>
/// The read-only deployment manifest for a single component (mod/dlc/bin). This is the
/// skeleton data model that connects the structure analyzer, the conflict evidence
/// analyzer, and (later) shared-resource reference counting. It is descriptive only: it
/// never moves/deletes/writes files and never special-cases a specific mod name.
/// </summary>
public sealed record ComponentManifest(
    string ComponentIdentity,
    TargetDomain Domain,
    string RootPath,
    IReadOnlyList<ComponentFile> Files)
{
    public int FileCount => Files.Count;
}