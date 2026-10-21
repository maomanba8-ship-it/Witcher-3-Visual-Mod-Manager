using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Import;

/// <summary>
/// One package's components, grouped the way the importer needs them: mods, dlcs, and the bin roots
/// whose contents all end up in the same folder in the game directory.
///
/// This exists so the importer can be driven by the analyzer alone. The old scanner decided the same
/// things from folder names, and giving both the same shape is what makes the switch comparable
/// instead of a rewrite nobody can verify.
/// </summary>
public sealed record ImportCandidateSet(
    IReadOnlyList<PackageComponent> Mods,
    IReadOnlyList<PackageComponent> Dlcs,
    IReadOnlyList<string> BinRoots,
    IReadOnlyList<string> Warnings)
{
    public bool IsEmpty => Mods.Count == 0 && Dlcs.Count == 0 && BinRoots.Count == 0;

    /// <summary>The component the record is built from: a mod when there is one, otherwise a dlc.</summary>
    public PackageComponent? Primary => Mods.Count > 0 ? Mods[0] : Dlcs.Count > 0 ? Dlcs[0] : null;
}

/// <summary>
/// Turns what the analyzer found into those groups, with the two rules the analyzer deliberately
/// does not decide for itself:
///
/// - a component with no file in it is dropped (installing an empty folder helps nobody and looks
///   like a broken install), and
/// - bin roots are kept as roots, because their file lists are relative to each root and flattening
///   them is exactly how two roots lose files to each other.
/// </summary>
public static class ImportCandidates
{
    public static ImportCandidateSet From(AnalysisResult? analysis, Func<string, int>? fileCountOf = null)
    {
        var count = fileCountOf ?? CountFiles;
        var mods = new List<PackageComponent>();
        var dlcs = new List<PackageComponent>();
        var binRoots = new List<string>();
        var warnings = new List<string>();

        foreach (var component in analysis?.Components ?? Array.Empty<PackageComponent>())
        {
            var root = (component.RootPath ?? "").Trim();
            if (root.Length == 0) continue;

            switch (component.Kind)
            {
                case ComponentKind.Bin:
                    // The analyzer only reports a bin root that holds files.
                    if (!binRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
                        binRoots.Add(root);
                    break;

                case ComponentKind.Mod:
                case ComponentKind.Dlc:
                    if (count(root) == 0)
                    {
                        warnings.Add($"component '{component.Name}' ({component.Kind}) holds no file");
                        continue;
                    }

                    var list = component.Kind == ComponentKind.Mod ? mods : dlcs;
                    if (!list.Any(c => string.Equals(c.RootPath, root, StringComparison.OrdinalIgnoreCase)))
                        list.Add(component);
                    break;
            }
        }

        // Ordered by name so the record built from the first mod is the same one every time (and
        // the same one the legacy scan would have picked).
        return new ImportCandidateSet(
            mods.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            dlcs.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            binRoots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
            warnings);
    }

    /// <summary>
    /// Relative paths that more than one bin root would write to. Two roots sharing a path would keep
    /// one file and silently lose the other once they are merged — the importer has to say so rather
    /// than pick a winner behind the user's back.
    /// </summary>
    public static IReadOnlyList<string> BinPathCollisions(
        IEnumerable<(string Root, IEnumerable<string> RelativePaths)>? roots)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, paths) in roots ?? Array.Empty<(string, IEnumerable<string>)>())
        {
            foreach (var path in (paths ?? Array.Empty<string>())
                         .Select(Normalize)
                         .Where(p => p.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                seen[path] = seen.TryGetValue(path, out var times) ? times + 1 : 1;
            }
        }

        return seen.Where(kv => kv.Value > 1)
            .Select(kv => kv.Key)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Paths inside a bin root, relative to it and spelled the way the index stores them.</summary>
    public static IReadOnlyList<string> RelativePathsUnder(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<string>();

        try
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Normalize(Path.GetRelativePath(root!, f)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Normalize(string? relativePath) =>
        (relativePath ?? "").Replace('\\', '/').Trim().TrimStart('/');

    private static int CountFiles(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
        }
        catch
        {
            return 0;
        }
    }
}
