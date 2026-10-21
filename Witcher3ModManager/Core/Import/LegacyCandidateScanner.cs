using System.IO;

namespace Witcher3ModManager.Core.Import;

/// <summary>One candidate root the legacy scanner found, with the opposite-kind folders inside it.</summary>
public sealed record LegacyCandidate(
    DirectoryInfo Root,
    IReadOnlyList<DirectoryInfo> AttachedDlcs,
    IReadOnlyList<DirectoryInfo> AttachedMods);

/// <summary>
/// The folder-name scanner the importer has used since the beginning, moved here verbatim so it can
/// be compared with the analyzer instead of trusted.
///
/// It is on its way out (stage 20): the analyzer decides the same things from structure and
/// evidence, and this class exists for two reasons only — the importer still calls it, and the
/// comparison tests need both sides in the same test project.
/// </summary>
public static class LegacyCandidateScanner
{
    /// <summary>All files under any bin/ directory, relative to the bin root they were found in.</summary>
    public static List<string> ScanBinFiles(string extractDir)
    {
        var binDirs = new DirectoryInfo(extractDir).GetDirectories("bin", SearchOption.AllDirectories);
        var results = new List<string>();
        foreach (var binDir in binDirs)
        {
            var files = Directory.GetFiles(binDir.FullName, "*.*", SearchOption.AllDirectories);
            results.AddRange(files.Select(f => f.Substring(binDir.FullName.Length + 1).Replace('\\', '/')));
        }
        return results;
    }

    /// <summary>
    /// Mod or dlc by name prefix, or by the container folder it sits in (a folder directly under
    /// dlc/ or mods/ is classified accordingly even when its name does not say so).
    /// </summary>
    public static (bool IsMod, bool IsDlc) Classify(DirectoryInfo dir)
    {
        var name = dir.Name;
        var parent = dir.Parent?.Name ?? "";

        if (name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase))
            return (false, true);
        if (name.StartsWith("mod", StringComparison.OrdinalIgnoreCase))
            return (true, false);
        if (parent.Equals("dlc", StringComparison.OrdinalIgnoreCase))
            return (false, true);
        if (parent.Equals("mods", StringComparison.OrdinalIgnoreCase))
            return (true, false);
        return (false, false);
    }

    public static bool IsCandidateDir(DirectoryInfo dir)
    {
        var (isMod, isDlc) = Classify(dir);
        if (!isMod && !isDlc) return false;

        // A component root must contain a recognized capability marker (content/metadata/scripts).
        return dir.GetDirectories("content", SearchOption.TopDirectoryOnly).Length > 0 ||
               dir.GetDirectories("metadata", SearchOption.TopDirectoryOnly).Length > 0 ||
               dir.GetDirectories("scripts", SearchOption.TopDirectoryOnly).Length > 0;
    }

    /// <summary>
    /// Read-only scan: outermost roots of each kind, with opposite-kind folders nested inside them
    /// attached (a mod carrying its own dlc becomes one candidate, not two).
    /// </summary>
    public static List<LegacyCandidate> Scan(string extractDir)
    {
        var allDirs = new DirectoryInfo(extractDir).GetDirectories("*", SearchOption.AllDirectories);
        var allCandidates = allDirs.Where(IsCandidateDir).ToList();

        var result = new List<LegacyCandidate>();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Shallowest first so outer containers are established before inner ones.
        var sorted = allCandidates.OrderBy(d => d.FullName.Length).ToList();

        foreach (var cand in sorted)
        {
            if (processed.Contains(cand.FullName)) continue;

            // Dedup same kind: keep the outermost.
            var isDedup = result.Any(existing =>
                Classify(cand).IsMod == Classify(existing.Root).IsMod &&
                cand.FullName.StartsWith(existing.Root.FullName + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));

            if (isDedup) { processed.Add(cand.FullName); continue; }

            var attachedDlcs = new List<DirectoryInfo>();
            var attachedMods = new List<DirectoryInfo>();
            processed.Add(cand.FullName);

            foreach (var inner in sorted)
            {
                if (processed.Contains(inner.FullName)) continue;
                if (!inner.FullName.StartsWith(
                        cand.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Same kind is handled by the dedup above.
                if (Classify(inner).IsMod == Classify(cand).IsMod) continue;

                if (Classify(inner).IsDlc) attachedDlcs.Add(inner);
                else attachedMods.Add(inner);
                processed.Add(inner.FullName);
            }

            result.Add(new LegacyCandidate(cand, attachedDlcs, attachedMods));
        }

        return result
            .OrderBy(mc => Classify(mc.Root).IsDlc ? 1 : 0)
            .ThenBy(mc => mc.Root.Name)
            .ToList();
    }
}
