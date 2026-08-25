using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Xunit;

namespace Witcher3ModManager.Tests;

public class PackageAnalyzerTests
{
    public static IEnumerable<object[]> FixtureDirs()
    {
        var root = FixturesAccess.FindDefaultFixturesRoot();
        foreach (var d in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(d, "expected.txt")))
                yield return new object[] { d };
        }
    }

    [Theory]
    [MemberData(nameof(FixtureDirs))]
    public void Analyze_matches_expected_components(string fixtureDir)
    {
        var expected = FixturesAccess.ParseExpectedComponents(Path.Combine(fixtureDir, "expected.txt"));
        var actual = PackageAnalyzer.Analyze(fixtureDir);

        var actualList = actual.Components
            .Select(c => (c.Kind.ToString(), c.Name))
            .OrderBy(x => x.Item1).ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expList = expected
            .OrderBy(x => x.Item1).ThenBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(expList, actualList);
    }

    [Theory]
    [MemberData(nameof(FixtureDirs))]
    public void Analyze_matches_expected_decisions_and_warnings(string fixtureDir)
    {
        var actual = PackageAnalyzer.Analyze(fixtureDir);
        var decision = string.Join("\n", actual.RequiresUserDecision);
        var warning = string.Join("\n", actual.Warnings);

        foreach (var raw in File.ReadAllLines(Path.Combine(fixtureDir, "expected.txt")))
        {
            var line = raw.Trim();
            if (line.StartsWith("Decision:", StringComparison.Ordinal))
            {
                var sub = line["Decision:".Length..].Trim();
                if (sub.Length > 0)
                    Assert.Contains(sub, decision, StringComparison.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("Warning:", StringComparison.Ordinal))
            {
                var sub = line["Warning:".Length..].Trim();
                if (sub.Length > 0)
                    Assert.Contains(sub, warning, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
