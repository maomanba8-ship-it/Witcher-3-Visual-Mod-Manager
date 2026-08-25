using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Manifest;

/// <summary>
/// Pure, read-only manifest builder. Given an extracted component root and its identity +
/// domain, it enumerates every file and records its normalized relative path, SHA-256 and
/// length. It NEVER moves/deletes/writes files, never calls 7-Zip/UI/storage/service, and
/// never special-cases a specific mod name. Rules are structural: what is under the root
/// is what gets recorded.
/// </summary>
public static class ManifestBuilder
{
    /// <summary>
    /// Fast, read-only file listing for import previews. Returns sorted relative paths in
    /// forward slashes (original case). It does NOT hash, so it is cheap enough to show a
    /// component's contents before the user confirms an import.
    /// </summary>
    public static IReadOnlyList<string> ListFiles(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(rootPath, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static ComponentManifest Build(string rootPath, string componentIdentity, TargetDomain domain)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Component root path is required.", nameof(rootPath));
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Component root not found: {rootPath}");

        var files = new List<ComponentFile>();
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
            var normalized = ConflictAnalyzer.NormalizePath(rel);
            var info = new FileInfo(file);
            string hash;
            using (var fs = File.OpenRead(file))
                hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            files.Add(new ComponentFile(domain, componentIdentity, normalized, hash, info.Length));
        }

        files.Sort((a, b) => string.Compare(
            a.NormalizedRelativePath, b.NormalizedRelativePath, StringComparison.Ordinal));
        return new ComponentManifest(componentIdentity, domain, rootPath, files);
    }
}