using System.IO;

namespace Witcher3ModManager.Core.Analysis;

/// <summary>
/// The deployment target domain for a component. Distinct namespaces; a mod and a dlc
/// that share a relative path are NOT a conflict.
/// </summary>
public enum TargetDomain
{
    Mods,
    Dlc,
    Bin
}

/// <summary>
/// The kind of component discovered in a package.
/// </summary>
public enum ComponentKind
{
    Mod,
    Dlc,
    Bin,
    Unsupported
}

/// <summary>
/// A single discovered component (mod/dlc/bin) and the evidence that supports it.
/// </summary>
public sealed record PackageComponent(
    string Name,
    ComponentKind Kind,
    string RootPath,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Result of a read-only package structure analysis. Purely descriptive, no side effects.
/// </summary>
public sealed record AnalysisResult(
    IReadOnlyList<PackageComponent> Components,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> RequiresUserDecision,
    string? RejectReason)
{
    public AnalysisResult()
        : this(Array.Empty<PackageComponent>(), Array.Empty<string>(), Array.Empty<string>(), null) { }
}

/// <summary>
/// Pure structure analyzer. Input: an already-extracted directory. Output: what was
/// discovered (with evidence) and what is ambiguous. It NEVER moves, deletes, writes,
/// shows UI, touches settings/storage/service, or calls 7-Zip, and it never special-cases
/// a specific mod name. Rules are expressed as root capabilities/markers.
/// </summary>
public static class PackageAnalyzer
{
    private static readonly string[] ModMarkers = { "content", "scripts", "metadata" };
    private static readonly string[] DlcMarkers = { "content", "metadata" };

    public static AnalysisResult Analyze(string extractDir)
    {
        if (string.IsNullOrWhiteSpace(extractDir) || !Directory.Exists(extractDir))
            return new AnalysisResult(
                Array.Empty<PackageComponent>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                "extract_dir_missing");

        var root = new DirectoryInfo(extractDir);
        var components = new List<PackageComponent>();
        var warnings = new List<string>();
        var decisions = new List<string>();

        // Bin is a separate domain; look for bin/ anywhere under the root.
        var binRoots = root.GetDirectories("bin", SearchOption.AllDirectories);
        foreach (var bin in binRoots)
        {
            var count = bin.GetFiles("*", SearchOption.AllDirectories).Length;
            if (count > 0)
                components.Add(new PackageComponent(bin.Name, ComponentKind.Bin, bin.FullName,
                    new[] { $"contains {count} file(s) under bin/" }));
        }

        // Mod/DLC are discovered by name pattern + capability markers.
        var candidateDirs = root.GetDirectories("*", SearchOption.AllDirectories)
            .OrderBy(d => d.FullName.Length)
            .ToArray();

        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in candidateDirs)
        {
            if (listed.Contains(dir.FullName)) continue;

            var name = dir.Name;
            var parentName = dir.Parent?.Name ?? "";

            // Pure container / marker directories are NOT components themselves.
            if (IsContainerName(name) || IsMarkerName(name))
                continue;

            // A folder sitting directly under a "dlc/" or "mods/" container is a DLC/mod
            // component even when its name does not start with dlc/mod (e.g. MagicSpellsRevamped
            // inside dlc/). This is a structural rule, not a per-mod special case.
            var nameMod = name.StartsWith("mod", StringComparison.OrdinalIgnoreCase);
            var nameDlc = name.StartsWith("dlc", StringComparison.OrdinalIgnoreCase);
            var parentIsDlc = parentName.Equals("dlc", StringComparison.OrdinalIgnoreCase);
            var parentIsMods = parentName.Equals("mods", StringComparison.OrdinalIgnoreCase);
            var hasModMarker = HasAnyMarker(dir, ModMarkers);

            ComponentKind kind;
            if (nameDlc || parentIsDlc)
                kind = ComponentKind.Dlc;
            else if (nameMod || parentIsMods)
                kind = ComponentKind.Mod;
            else
                kind = hasModMarker ? ComponentKind.Mod : ComponentKind.Unsupported;

            if (kind != ComponentKind.Unsupported)
            {
                var evidence = new List<string>();
                if (nameMod) evidence.Add("name starts with mod");
                if (nameDlc) evidence.Add("name starts with dlc");
                if (parentIsDlc) evidence.Add("parent folder is dlc/");
                if (parentIsMods) evidence.Add("parent folder is mods/");
                if (hasModMarker) evidence.Add("contains content/scripts/metadata marker");

                // Only register the outermost container for this kind (avoid reporting an
                // inner same-type folder that lives inside an already-registered component).
                var alreadyCovered = components.Any(c =>
                    c.Kind == kind &&
                    dir.FullName.StartsWith(c.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (!alreadyCovered)
                    components.Add(new PackageComponent(name, kind, dir.FullName, evidence));
                listed.Add(dir.FullName);
            }
        }

        // Unknown structure: no mod/dlc/bin found.
        var hasAny = components.Count > 0;
        if (!hasAny)
        {
            var top = root.GetDirectories("*").Select(d => d.Name).ToArray();
            if (top.Length == 0)
                return new AnalysisResult(Array.Empty<PackageComponent>(), Array.Empty<string>(), Array.Empty<string>(),
                    "empty_package");

            // Could not identify any game component.
            return new AnalysisResult(
                Array.Empty<PackageComponent>(),
                new[] { "no recognizable mod/dlc/bin component found" },
                Array.Empty<string>(),
                "unknown_structure");
        }

        return new AnalysisResult(components, warnings, decisions, null);
    }

    private static bool HasAnyMarker(DirectoryInfo dir, string[] markers)
    {
        foreach (var marker in markers)
            if (dir.GetDirectories(marker, SearchOption.TopDirectoryOnly).Length > 0)
                return true;
        return false;
    }

    private static bool IsContainerName(string name)
    {
        return name.Equals("dlc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("mods", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarkerName(string name)
    {
        return name.Equals("content", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("scripts", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("metadata", StringComparison.OrdinalIgnoreCase);
    }
}
