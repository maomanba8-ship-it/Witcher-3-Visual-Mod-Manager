using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Manifest;

/// <summary>
/// Pure content fingerprint for a component's file manifest. It digests the ordered set of
/// (normalized relative path, sha256) pairs, so two components imported from the same bytes
/// share a fingerprint and two that differed do not. It is deliberately content-only: the
/// component identity and domain are NOT part of the digest, which is what makes a shared
/// physical folder comparable across its owners. Empty input yields "" (unknown); callers
/// must treat that as "cannot determine", never as a match. It never reads the file system.
/// </summary>
public static class ComponentFingerprint
{
    public static string Compute(IEnumerable<ComponentFile> files)
    {
        if (files is null) return "";

        var lines = files
            .Select(f => ConflictAnalyzer.NormalizePath(f.NormalizedRelativePath)
                         + "\u0000"
                         + (f.Sha256 ?? "").ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        if (lines.Count == 0) return "";

        var payload = string.Join("\n", lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string Compute(ComponentManifest manifest)
    {
        if (manifest is null) return "";
        return Compute(manifest.Files);
    }

    /// <summary>
    /// True only when both sides are known and differ. An unknown side never reports a
    /// difference: history that was never recorded must not be presented as evidence.
    /// </summary>
    public static bool Differs(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
        return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
