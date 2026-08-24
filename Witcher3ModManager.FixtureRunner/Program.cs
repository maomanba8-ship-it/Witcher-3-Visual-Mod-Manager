using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;

internal static class Program
{
    private static int Main(string[] args)
    {
        var fixturesRoot = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? Path.GetFullPath(args[0])
            : FindDefaultFixturesRoot();

        if (string.IsNullOrEmpty(fixturesRoot) || !Directory.Exists(fixturesRoot))
        {
            Console.Error.WriteLine("Fixtures root not found. Pass it as an argument or run from a project checkout.");
            return 2;
        }

        var failure = 0;

        Console.WriteLine("=== PackageAnalyzer fixture checks ===");
        foreach (var fixtureDir in Directory.GetDirectories(fixturesRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(fixtureDir);
            var expectedPath = Path.Combine(fixtureDir, "expected.txt");
            if (!File.Exists(expectedPath)) { Console.WriteLine($"[SKIP] {name}: no expected.txt"); continue; }

            var expected = ParseExpectedComponents(expectedPath);
            var actual = PackageAnalyzer.Analyze(fixtureDir);
            var actualList = actual.Components
                .Select(c => (c.Kind.ToString(), c.Name))
                .OrderBy(x => x.Item1).ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var expList = expected
                .OrderBy(x => x.Item1).ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ok = expList.SequenceEqual(actualList);
            var rej = actual.RejectReason == null ? "" : " reject=" + actual.RejectReason;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: expected=[{string.Join(", ", expList)}] actual=[{string.Join(", ", actualList)}]{rej}");
            if (!ok) failure++;
        }

        Console.WriteLine();
        Console.WriteLine("=== ConflictAnalyzer case checks ===");
        foreach (var c in ConflictCases)
        {
            var result = ConflictAnalyzer.Analyze(c.Files);
            var kinds = result.Select(r => r.Kind.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var expKinds = c.ExpectedKinds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var ok = expKinds.SequenceEqual(kinds);
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {c.Name}: expected=[{string.Join(", ", expKinds)}] actual=[{string.Join(", ", kinds)}]");
            if (!ok) failure++;
        }

        Console.WriteLine();
        if (failure == 0) { Console.WriteLine("ALL OK"); return 0; }
        Console.WriteLine($"FAILURES: {failure}");
        return 1;
    }

    private static string? FindDefaultFixturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Witcher3ModManager", "Core", "Analysis", "Fixtures")))
                return Path.Combine(dir.FullName, "Witcher3ModManager", "Core", "Analysis", "Fixtures");
            dir = dir.Parent;
        }
        return null;
    }

    private static List<(string, string)> ParseExpectedComponents(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Any(l => l.Contains("(none recognized)", StringComparison.OrdinalIgnoreCase)))
            return new List<(string, string)>();

        var result = new List<(string, string)>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("- ", StringComparison.Ordinal)) t = t[2..].Trim();
            var idx = t.IndexOf(':');
            if (idx < 0) continue;
            var kind = t[..idx].Trim();
            var name = t[(idx + 1)..].Trim();
            if (kind.Length == 0 || name.Length == 0) continue;
            result.Add((kind, name));
        }
        return result;
    }

    private sealed record ConflictCase(string Name, IReadOnlyList<ComponentFile> Files, IReadOnlyList<string> ExpectedKinds);

    private static readonly ConflictCase[] ConflictCases = new[]
    {
        new ConflictCase("mod_vs_dlc_same_path_diff_hash", new[]
        {
            new ComponentFile(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
            new ComponentFile(TargetDomain.Dlc, "dlcA", "content/a.txt", "shaB", 1),
        }, new string[] { }),
        new ConflictCase("same_domain_same_hash", new[]
        {
            new ComponentFile(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
            new ComponentFile(TargetDomain.Mods, "modB", "content/a.txt", "shaA", 1),
        }, new string[] { "DuplicateContent" }),
        new ConflictCase("same_domain_diff_hash", new[]
        {
            new ComponentFile(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
            new ComponentFile(TargetDomain.Mods, "modB", "content/a.txt", "shaB", 1),
        }, new string[] { "DefiniteOverride" }),
        new ConflictCase("same_domain_ws_diff_hash", new[]
        {
            new ComponentFile(TargetDomain.Mods, "modA", "scripts/x.ws", "shaA", 1),
            new ComponentFile(TargetDomain.Mods, "modB", "scripts/x.ws", "shaB", 1),
        }, new string[] { "ScriptOverride" }),
        new ConflictCase("same_component_dup_file", new[]
        {
            new ComponentFile(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
            new ComponentFile(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
        }, new string[] { }),
    };
}
