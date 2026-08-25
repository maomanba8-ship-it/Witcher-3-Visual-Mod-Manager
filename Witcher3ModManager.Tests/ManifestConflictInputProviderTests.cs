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
}