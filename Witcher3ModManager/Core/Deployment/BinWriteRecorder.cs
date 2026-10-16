using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>One bin file an enable wrote, and what was there before it.</summary>
public sealed record BinWrite(string Path, string Sha256, string? ShaBefore);

/// <summary>
/// Works out what one enable actually wrote into the game's bin directory.
///
/// Two rules, both learned the hard way:
///
/// 1. Only steps that ran count. A file this operation did not copy must not end up claimed as
///    ours, or a later stop would "restore" something we never touched.
/// 2. A write whose content cannot be established is dropped rather than guessed — but reading the
///    file back is not the only way to know: what was copied *is* the source's bytes, so the source
///    is used when reading back fails. Without that fallback a successful enable could leave no
///    record at all, and the file would look like one nobody ever wrote.
/// </summary>
public static class BinWriteRecorder
{
    public static IReadOnlyList<BinWrite> From(
        DeploymentResult? result,
        IReadOnlyDictionary<string, string?>? capturedOriginals,
        Func<string, string?>? hashOf = null)
    {
        if (result is null || capturedOriginals is null || capturedOriginals.Count == 0)
            return Array.Empty<BinWrite>();

        var hash = hashOf ?? BinBackupStore.HashOf;
        var writes = new List<BinWrite>();

        foreach (var step in (result.Steps ?? Array.Empty<DeploymentStepResult>())
                     .Where(s => s.Domain == TargetDomain.Bin && s.Outcome == DeploymentStepOutcome.Done))
        {
            // No captured state means we cannot say what this replaced, so we say nothing at all.
            if (!capturedOriginals.TryGetValue(step.Subject, out var before)) continue;

            var written = hash(step.Target);
            if (written is null && !string.IsNullOrEmpty(step.SourcePath))
                written = hash(step.SourcePath);
            if (written is null) continue;

            writes.Add(new BinWrite(step.Subject, written, before));
        }

        return writes;
    }
}
