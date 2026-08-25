using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Witcher3ModManager.Tests;

/// <summary>
/// Locates the anonymous package-structure fixtures and parses expected.txt, so the
/// xUnit assertions mirror what the zero-dependency FixtureRunner already validates.
/// </summary>
internal static class FixturesAccess
{
    public static string FindDefaultFixturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Witcher3ModManager", "Core", "Analysis", "Fixtures")))
                return Path.Combine(dir.FullName, "Witcher3ModManager", "Core", "Analysis", "Fixtures");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Fixtures root not found. Run tests from the repo checkout.");
    }

    public static List<(string Kind, string Name)> ParseExpectedComponents(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Any(l => l.Contains("(none recognized)", StringComparison.OrdinalIgnoreCase)))
            return new List<(string, string)>();

        var result = new List<(string, string)>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            // Only component lines use "- Kind: Name"; Decision:/Warning: are metadata, not components.
            if (!t.StartsWith("- ", StringComparison.Ordinal)) continue;
            t = t[2..].Trim();
            var idx = t.IndexOf(':');
            if (idx < 0) continue;
            var kind = t[..idx].Trim();
            var name = t[(idx + 1)..].Trim();
            if (kind.Length == 0 || name.Length == 0) continue;
            result.Add((kind, name));
        }
        return result;
    }
}
