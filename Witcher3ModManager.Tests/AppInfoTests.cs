using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core;
using Xunit;

namespace Witcher3ModManager.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_is_a_bare_semver_and_display_version_prefixes_it()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
        Assert.Equal("v" + AppInfo.Version, AppInfo.DisplayVersion);
    }

    [Fact]
    public void The_version_literal_lives_only_in_AppInfo()
    {
        // Guards the whole point of having a single source: the UI, the localisation tables and
        // the XAML must all read AppInfo, never restate the number. This is how the version once
        // ended up hardcoded in eight places and drifted for three months.
        var appRoot = FindAppProjectRoot();
        var offenders = SourceFiles(appRoot)
            .Where(f => !string.Equals(Path.GetFileName(f), "AppInfo.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains(AppInfo.Version, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(appRoot, f))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsGeneratedOutput(f));

    private static bool IsGeneratedOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                              p.StartsWith("publish_", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindAppProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Witcher3ModManager", "AppDataPath.cs");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "Witcher3ModManager");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("App project root not found. Run tests from the repo checkout.");
    }
}
