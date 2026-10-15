using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Manifest;

/// <summary>
/// Read-only collector that turns one component root on disk into a persistable fingerprint
/// entry. It reads and hashes files but never moves, deletes or writes anything, and never
/// special-cases a mod name.
///
/// Callers must run it while the component root still holds that package's own bytes. Once a
/// later import overwrites a shared storage folder, the earlier package's content is gone and
/// can no longer be fingerprinted, so recording after the move would silently describe the
/// wrong package.
/// </summary>
public static class ComponentFingerprintCollector
{
    /// <summary>
    /// Returns null when the folder name or root is unusable (blank, missing, or contains no
    /// files), so callers record only what they could actually observe.
    /// </summary>
    public static ComponentFingerprintEntry? FromRoot(
        TargetDomain domain,
        string? folderName,
        string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;
        if (string.IsNullOrWhiteSpace(rootPath)) return null;
        if (!Directory.Exists(rootPath)) return null;

        var manifest = ManifestBuilder.Build(rootPath, folderName, domain);
        var fingerprint = ComponentFingerprint.Compute(manifest);
        if (string.IsNullOrEmpty(fingerprint)) return null;

        return new ComponentFingerprintEntry(domain, folderName, fingerprint, manifest.FileCount);
    }
}
