using System;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Manifest;
using Witcher3ModManager.Core.Shared;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ManifestConflictInputProviderTests
{
    [Fact]
    public void Build_returns_component_files_for_each_claim()
    {
        var storage = Path.Combine(Path.GetTempPath(), "w3mm_provider_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(storage, "modFoo", "content"));
            Directory.CreateDirectory(Path.Combine(storage, "dlcBar", "scripts"));
            File.WriteAllText(Path.Combine(storage, "modFoo", "content", "a.txt"), "AAA");
            File.WriteAllText(Path.Combine(storage, "dlcBar", "scripts", "x.ws"), "SCRIPT");

            var claims = new FolderClaim[]
            {
                new("modId1", TargetDomain.Mods, "modFoo"),
                new("modId2", TargetDomain.Dlc, "dlcBar"),
            };

            var files = ManifestConflictInputProvider.Build(storage, claims);

            Assert.Equal(2, files.Count);

            var modA = files.Single(f => f.Domain == TargetDomain.Mods);
            Assert.Equal("modId1", modA.ComponentIdentity);
            Assert.Equal("content/a.txt", modA.NormalizedRelativePath);
            Assert.Equal(3, modA.Length);

            var dlcB = files.Single(f => f.Domain == TargetDomain.Dlc);
            Assert.Equal("modId2", dlcB.ComponentIdentity);
            Assert.Equal("scripts/x.ws", dlcB.NormalizedRelativePath);
            Assert.Equal(6, dlcB.Length);
        }
        finally
        {
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
        }
    }

    [Fact]
    public void Build_skips_missing_folder_claims()
    {
        var storage = Path.Combine(Path.GetTempPath(), "w3mm_provider_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(storage);
            var claims = new FolderClaim[]
            {
                new("modId1", TargetDomain.Mods, "missingFolder"),
            };

            Assert.Empty(ManifestConflictInputProvider.Build(storage, claims));
        }
        finally
        {
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
        }
    }

    [Fact]
    public void Build_returns_empty_for_null_or_empty_storage()
    {
        var claims = new FolderClaim[]
        {
            new("modId1", TargetDomain.Mods, "modFoo"),
        };

        Assert.Empty(ManifestConflictInputProvider.Build("", claims));
        Assert.Empty(ManifestConflictInputProvider.Build(null!, claims));
        Assert.Empty(ManifestConflictInputProvider.Build(@"C:\does-not-exist", null!));
    }

    [Fact]
    public void Build_resolves_legacy_unprefixed_storage_folder()
    {
        var storage = Path.Combine(Path.GetTempPath(), "w3mm_provider_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(storage, "EnableJumpInWater", "content", "scripts"));
            File.WriteAllText(Path.Combine(storage, "EnableJumpInWater", "content", "scripts", "r4Player.ws"), "SCRIPT");

            var claims = new FolderClaim[]
            {
                new("modId1", TargetDomain.Mods, "modEnableJumpInWater"),
            };

            var files = ManifestConflictInputProvider.Build(storage, claims);

            var f = Assert.Single(files);
            Assert.Equal("modId1", f.ComponentIdentity);
            Assert.Equal("content/scripts/r4player.ws", f.NormalizedRelativePath);
            Assert.Equal(TargetDomain.Mods, f.Domain);
        }
        finally
        {
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
        }
    }

    [Fact]
    public void Build_does_not_double_claim_same_folder_for_mod_and_dlc()
    {
        var storage = Path.Combine(Path.GetTempPath(), "w3mm_provider_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(storage, "DhusRoach", "content"));
            File.WriteAllText(Path.Combine(storage, "DhusRoach", "content", "a.txt"), "A");

            var claims = new FolderClaim[]
            {
                new("modId1", TargetDomain.Mods, "modDhusRoach"),
                new("modId1", TargetDomain.Dlc, "dlcDhusRoach"),
            };

            var files = ManifestConflictInputProvider.Build(storage, claims);

            Assert.Single(files); // same physical folder attributed once per component
            Assert.Equal(TargetDomain.Mods, files[0].Domain);
        }
        finally
        {
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
        }
    }

    [Fact]
    public void BuildFromRoots_records_files_under_the_claimed_domain()
    {
        var root = CreateTempRoot(("config/r4game/user_config_matrix/pc/modX.xml", "<xml/>"));
        try
        {
            var claims = new ComponentRootClaim[]
            {
                new("modId1", TargetDomain.Bin, root),
            };

            var files = ManifestConflictInputProvider.BuildFromRoots(claims);

            var f = Assert.Single(files);
            Assert.Equal(TargetDomain.Bin, f.Domain);
            Assert.Equal("modId1", f.ComponentIdentity);
            Assert.Equal("config/r4game/user_config_matrix/pc/modx.xml", f.NormalizedRelativePath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BuildFromRoots_skips_blank_missing_and_duplicate_roots()
    {
        var root = CreateTempRoot(("config/a.xml", "A"));
        var missing = Path.Combine(Path.GetTempPath(), "w3mm_missing_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Assert.Empty(ManifestConflictInputProvider.BuildFromRoots(null!));

            var files = ManifestConflictInputProvider.BuildFromRoots(new ComponentRootClaim[]
            {
                new("modId1", TargetDomain.Bin, ""),
                new("modId2", TargetDomain.Bin, missing),
                new("modId3", TargetDomain.Bin, root),
                new("modId3", TargetDomain.Bin, root),
            });

            Assert.Single(files); // the same root is claimed once per component
            Assert.Equal("modId3", files[0].ComponentIdentity);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Bin_collision_between_two_records_is_a_real_override()
    {
        var rootA = CreateTempRoot(("config/r4game/user_config_matrix/pc/shared.xml", "<a/>"));
        var rootB = CreateTempRoot(("config/r4game/user_config_matrix/pc/shared.xml", "<b/>"));
        try
        {
            var files = ManifestConflictInputProvider.BuildFromRoots(new ComponentRootClaim[]
            {
                new("modAId", TargetDomain.Bin, rootA),
                new("modBId", TargetDomain.Bin, rootB),
            });

            var evidence = Assert.Single(ConflictAnalyzer.Analyze(files));
            Assert.Equal(ConflictKind.BinOverrideRisk, evidence.Kind);
            Assert.True(ConflictEvidenceRules.IsRealConflictKind(evidence.Kind));
            Assert.Equal("config/r4game/user_config_matrix/pc/shared.xml", evidence.NormalizedRelativePath);
        }
        finally
        {
            if (Directory.Exists(rootA)) Directory.Delete(rootA, true);
            if (Directory.Exists(rootB)) Directory.Delete(rootB, true);
        }
    }

    [Fact]
    public void Bin_collision_with_identical_content_is_not_a_conflict()
    {
        var rootA = CreateTempRoot(("config/r4game/user_config_matrix/pc/shared.xml", "<same/>"));
        var rootB = CreateTempRoot(("config/r4game/user_config_matrix/pc/shared.xml", "<same/>"));
        try
        {
            var files = ManifestConflictInputProvider.BuildFromRoots(new ComponentRootClaim[]
            {
                new("modAId", TargetDomain.Bin, rootA),
                new("modBId", TargetDomain.Bin, rootB),
            });

            var evidence = Assert.Single(ConflictAnalyzer.Analyze(files));
            Assert.Equal(ConflictKind.DuplicateContent, evidence.Kind);
            Assert.False(ConflictEvidenceRules.IsRealConflictKind(evidence.Kind));
        }
        finally
        {
            if (Directory.Exists(rootA)) Directory.Delete(rootA, true);
            if (Directory.Exists(rootB)) Directory.Delete(rootB, true);
        }
    }

    private static string CreateTempRoot(params (string Rel, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_binroot_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }
}
