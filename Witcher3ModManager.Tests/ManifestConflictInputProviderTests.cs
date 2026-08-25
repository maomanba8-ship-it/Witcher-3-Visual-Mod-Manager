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
}