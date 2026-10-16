using System.IO;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Manifest;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// How this manager answers "is what is on disk still what we wrote?".
///
/// A single file is its own content hash. A folder cannot be: what matters is the set of files and
/// their contents, in a form that does not depend on the order a listing happens to come back in,
/// which is what the component fingerprint is for. Both are computed the same way here as when the
/// index was written, or the comparison would be meaningless.
/// </summary>
public static class DeployedContent
{
    /// <summary>Content hash of a file, or null when it is not there or cannot be read.</summary>
    public static string? FileHash(string? path) => BinBackupStore.HashOf(path);

    /// <summary>
    /// Fingerprint of a deployed folder, or null when there is no folder to fingerprint. Never
    /// throws: an unreadable folder is reported as "no fingerprint", which the caller turns into
    /// "not the content we wrote" rather than into a failed audit.
    /// </summary>
    public static string? FolderFingerprint(string? path, string packageId, TargetDomain domain)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;

        try
        {
            return ComponentFingerprint.Compute(ManifestBuilder.Build(path!, packageId, domain));
        }
        catch
        {
            return null;
        }
    }
}
