using System.IO;
using System.Text.RegularExpressions;

namespace Witcher3ModManager.Core.Analysis.Script;

/// <summary>One enabled mod that references a class defined by the mod being disabled.</summary>
public sealed record ScriptDependency(string DependentModName, string ClassName);

/// <summary>
/// Finds loose-script dependencies: a class defined in one mod and referenced by another.
///
/// Disabling the definer leaves the dependent script without the class, which is a compile error
/// at the next game start. The check is deliberately narrow — only class names in loose .ws files,
/// no guessing from names or folders — so a hit is evidence, not a heuristic.
/// </summary>
public static partial class ScriptDependencyInspector
{
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"(?:^|[\r\n])\s*class\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ClassDefinition();

    public static IReadOnlyList<ScriptDependency> FindDependents(
        string definingRoot,
        IEnumerable<(string ModName, string Root)> otherComponents)
    {
        var classes = ClassesDefinedIn(definingRoot);
        if (classes.Count == 0) return Array.Empty<ScriptDependency>();

        var definingFull = SafeFullPath(definingRoot);
        var found = new List<ScriptDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (modName, root) in otherComponents)
        {
            if (string.IsNullOrWhiteSpace(modName) || string.IsNullOrWhiteSpace(root)) continue;
            if (string.Equals(SafeFullPath(root), definingFull, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Directory.Exists(root)) continue;

            foreach (var file in EnumerateScripts(root))
            {
                var text = TryRead(file);
                if (text is null) continue;
                text = StripComments(text);

                foreach (var className in classes)
                {
                    if (!Regex.IsMatch(text, $@"\b{Regex.Escape(className)}\b")) continue;
                    if (seen.Add(modName + "\u0001" + className))
                        found.Add(new ScriptDependency(modName, className));
                }
            }
        }

        return found;
    }

    public static IReadOnlyList<string> ClassesDefinedIn(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateScripts(root))
        {
            var text = TryRead(file);
            if (text is null) continue;

            foreach (Match match in ClassDefinition().Matches(StripComments(text)))
                names.Add(match.Groups[1].Value);
        }

        return names.ToList();
    }

    private static IEnumerable<string> EnumerateScripts(string root)
    {
        try
        {
            return Directory.GetFiles(root, "*.ws", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string StripComments(string text) =>
        LineComment().Replace(BlockComment().Replace(text, " "), "");

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path;
        }
    }
}
