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
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Bin, "modA", "bundles/foo.bundle", "shaA", 1),
                new(TargetDomain.Bin, "modB", "bundles/foo.bundle", "shaA", 1),
            },
            new[] { "DuplicateContent" }
        };
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Bin, "modA", "bundles/foo.bundle", "shaA", 1),
                new(TargetDomain.Bin, "modB", "bundles/foo.bundle", "shaB", 1),
            },
            new[] { "UnsupportedContainer" }
        };

        // Known auto-generated WolvenKit artifacts: same path, different hash.
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/en.w3strings", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/en.w3strings", "shaB", 1),
            },
            new[] { "GeneratedArtifactDiff" }
        };
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/metadata.store", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/metadata.store", "shaB", 1),
            },
            new[] { "GeneratedArtifactDiff" }
        };
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/texture.cache", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/texture.cache", "shaB", 1),
            },
            new[] { "GeneratedArtifactDiff" }
        };
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/en.w3strings", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/en.w3strings", "shaA", 1),
            },
            new[] { "DuplicateContent" }
        };

        // Opaque bundle with different hash stays UnsupportedContainer (not downgraded).
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/blob0.bundle", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/blob0.bundle", "shaB", 1),
            },
            new[] { "UnsupportedContainer" }
        };

        // Bin config files (mod menu XMLs) are not opaque containers: enabling copies over the
        // game file in place and disabling never undoes it, so the same path with different
        // content is a real override risk rather than an informational overlap.
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Bin, "modA", "config/r4game/user_config_matrix/pc/shared.xml", "shaA", 1),
                new(TargetDomain.Bin, "modB", "config/r4game/user_config_matrix/pc/shared.xml", "shaB", 1),
            },
            new[] { "BinOverrideRisk" }
        };
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Bin, "modA", "config/r4game/user_config_matrix/pc/shared.xml", "shaA", 1),
                new(TargetDomain.Bin, "modB", "config/r4game/user_config_matrix/pc/shared.xml", "shaA", 1),
            },
            new[] { "DuplicateContent" }
        };
        // A bin path that is itself an opaque container keeps the container verdict.
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Bin, "modA", "x64/patch.bundle", "shaA", 1),
                new(TargetDomain.Bin, "modB", "x64/patch.bundle", "shaB", 1),
            },
            new[] { "UnsupportedContainer" }
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
