using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Import;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ImportCandidatesTests
{
    [Fact]
    public void Components_are_split_into_mods_dlcs_and_bin_roots()
    {
        var set = From(
            Component("modA", ComponentKind.Mod, @"C:\x\modA"),
            Component("dlcA", ComponentKind.Dlc, @"C:\x\dlcA"),
            Component("bin", ComponentKind.Bin, @"C:\x\bin"));

        Assert.Equal("modA", Assert.Single(set.Mods).Name);
        Assert.Equal("dlcA", Assert.Single(set.Dlcs).Name);
        Assert.Equal(@"C:\x\bin", Assert.Single(set.BinRoots));
        Assert.Empty(set.Warnings);
    }

    [Fact]
    public void A_component_with_no_file_is_dropped_with_a_warning()
    {
        // The analyzer would call a 'mod'-named empty folder a Mod; installing it would create an
        // empty mod folder that the game ignores and the user cannot explain.
        var set = ImportCandidates.From(
            new AnalysisResult(new[] { Component("modEmpty", ComponentKind.Mod, @"C:\x\modEmpty") },
                Array.Empty<string>(), Array.Empty<string>(), null),
            fileCountOf: _ => 0);

        Assert.Empty(set.Mods);
        Assert.Single(set.Warnings);
        Assert.Contains("modEmpty", set.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_primary_component_prefers_a_mod()
    {
        var set = From(
            Component("dlcA", ComponentKind.Dlc, @"C:\x\dlcA"),
            Component("modA", ComponentKind.Mod, @"C:\x\modA"));

        Assert.Equal("modA", set.Primary!.Name);
    }

    [Fact]
    public void Without_components_the_primary_is_null()
    {
        Assert.Null(ImportCandidates.From(null).Primary);
        Assert.True(ImportCandidates.From(new AnalysisResult()).IsEmpty);
    }

    [Fact]
    public void Unsupported_components_are_left_out()
    {
        var set = From(Component("weird", ComponentKind.Unsupported, @"C:\x\weird"));

        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void Duplicate_roots_are_kept_once()
    {
        var set = From(
            Component("modA", ComponentKind.Mod, @"C:\x\modA"),
            Component("modA", ComponentKind.Mod, @"c:\x\moda"));

        Assert.Single(set.Mods);
    }

    [Fact]
    public void Two_bin_roots_writing_the_same_path_are_reported()
    {
        var collisions = ImportCandidates.BinPathCollisions(new[]
        {
            (@"C:\a", (IEnumerable<string>)new[] { "config/input.xml", "config/other.xml" }),
            (@"C:\b", (IEnumerable<string>)new[] { @"config\input.xml" })
        });

        Assert.Equal("config/input.xml", Assert.Single(collisions));
    }

    [Fact]
    public void Distinct_bin_paths_do_not_collide()
    {
        var collisions = ImportCandidates.BinPathCollisions(new[]
        {
            (@"C:\a", (IEnumerable<string>)new[] { "config/a.xml" }),
            (@"C:\b", (IEnumerable<string>)new[] { "config/b.xml" })
        });

        Assert.Empty(collisions);
    }

    [Fact]
    public void A_path_listed_twice_inside_one_root_is_not_a_collision()
    {
        var collisions = ImportCandidates.BinPathCollisions(new[]
        {
            (@"C:\a", (IEnumerable<string>)new[] { "config/a.xml", "config/a.xml" })
        });

        Assert.Empty(collisions);
    }

    [Fact]
    public void Relative_paths_are_normalized_when_read_from_disk()
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_cand_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "a.xml"), "x");
        try
        {
            Assert.Equal(new[] { "config/a.xml" }, ImportCandidates.RelativePathsUnder(root));
            Assert.Empty(ImportCandidates.RelativePathsUnder(Path.Combine(root, "missing")));
            Assert.Empty(ImportCandidates.RelativePathsUnder(null));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    private static ImportCandidateSet From(params PackageComponent[] components) =>
        ImportCandidates.From(new AnalysisResult(components, Array.Empty<string>(),
            Array.Empty<string>(), null), fileCountOf: _ => 1);

    private static PackageComponent Component(string name, ComponentKind kind, string root) =>
        new(name, kind, root, Array.Empty<string>());
}
