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
                new(TargetDomain.Mods, "modA", "content/a.dat", "shaA", 1),
                new(TargetDomain.Dlc, "dlcA", "content/a.dat", "shaB", 1),
            },
            Array.Empty<string>()
        };

        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/a.dat", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/a.dat", "shaA", 1),
            },
            new[] { "DuplicateContent" }
        };

        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/a.dat", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/a.dat", "shaB", 1),
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
                new(TargetDomain.Mods, "modA", "content/a.dat", "shaA", 1),
                new(TargetDomain.Mods, "modA", "content/a.dat", "shaA", 1),
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

        // The real Walk by Default / No Map Borders pair: the same three content files with
        // different bytes. Nothing here is a hard conflict — the two blobs touch different
        // symbols, which Script Merger confirms.
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modwalkbydefault", "content/info.json", "shaI1", 399),
                new(TargetDomain.Mods, "modnomapborders", "content/info.json", "shaI2", 379),
                new(TargetDomain.Mods, "modwalkbydefault", "content/metadata.store", "shaM1", 114),
                new(TargetDomain.Mods, "modnomapborders", "content/metadata.store", "shaM2", 114),
                new(TargetDomain.Mods, "modwalkbydefault", "content/precompiled.rsblob", "shaR1", 165),
                new(TargetDomain.Mods, "modnomapborders", "content/precompiled.rsblob", "shaR2", 241),
            },
            new[] { "GeneratedArtifactDiff", "GeneratedArtifactDiff", "UnsupportedContainer" }
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

        // 5.0 precompiled scripts arrive as content/precompiled.rsblob. The blob is a container
        // of compiled functions: two different blobs only conflict when their symbols overlap,
        // which a file hash cannot tell. It is therefore an opaque container, not a script
        // override (Walk by Default and No Map Borders both ship a blob and do not conflict).
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "content/precompiled.rsblob", "shaA", 1),
                new(TargetDomain.Mods, "modB", "content/precompiled.rsblob", "shaB", 1),
            },
            new[] { "UnsupportedContainer" }
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
        // Documentation never reaches the game, so two mods each shipping one is not a conflict.
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modPetCat", "README.txt", "shaA", 1),
                new(TargetDomain.Mods, "modFullRefund", "README.txt", "shaB", 1),
            },
            Array.Empty<string>()
        };
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "readme", "shaA", 1),
                new(TargetDomain.Mods, "modB", "README", "shaB", 1),
            },
            Array.Empty<string>()
        };
        yield return new object[]
        {
            new ComponentFile[]
            {
                new(TargetDomain.Mods, "modA", "notes.md", "shaA", 1),
                new(TargetDomain.Mods, "modB", "notes.md", "shaB", 1),
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

