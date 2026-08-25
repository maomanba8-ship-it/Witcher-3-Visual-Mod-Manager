using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ConflictAnalyzerTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
                new(TargetDomain.Dlc, "dlcA", "content/a.txt", "shaB", 1),
            },
            Array.Empty<string>()
        };

        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/a.txt", "shaA", 1),
            },
            new[] { "DuplicateContent" }
        };

        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/a.txt", "shaB", 1),
            },
            new[] { "DefiniteOverride" }
        };

        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "scripts/x.ws", "shaA", 1),
                new(TargetDomain.Mods, "modB", "scripts/x.ws", "shaB", 1),
            },
            new[] { "ScriptOverride" }
        };

        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
                new(TargetDomain.Mods, "modA", "content/a.txt", "shaA", 1),
            },
            Array.Empty<string>()
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Analyze_matches_expected_kinds(ComponentFile[] files, string[] expectedKinds)
    {
        var result = ConflictAnalyzer.Analyze(files);
        var kinds = result.Select(r => r.Kind.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var expKinds = expectedKinds.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(expKinds, kinds);
    }
}
