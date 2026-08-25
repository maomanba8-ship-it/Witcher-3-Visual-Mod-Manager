using System;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Shared;
using Xunit;

namespace Witcher3ModManager.Tests;

public class SharedResourceResolverTests
{
    [Fact]
    public void Two_components_sharing_a_folder_are_shared()
    {
        var claims = new FolderClaim[]
        {
            new("modHerm", TargetDomain.Mods, "mod_EH_RiderUtil"),
            new("modGry", TargetDomain.Mods, "mod_EH_RiderUtil"),
        };

        var result = SharedResourceResolver.Resolve(claims);

        var f = Assert.Single(result);
        Assert.True(f.IsShared);
        Assert.Equal(2, f.RefCount);
        Assert.Equal(new[] { "modGry", "modHerm" }, f.Owners);
        Assert.True(f.IsOwnedBy("modHerm"));
        Assert.True(f.IsOwnedBy("modgry"));
    }

    [Fact]
    public void Single_owner_is_not_shared()
    {
        var claims = new FolderClaim[]
        {
            new("modHerm", TargetDomain.Mods, "mod_EH_RiderUtil"),
        };

        var f = Assert.Single(SharedResourceResolver.Resolve(claims));
        Assert.False(f.IsShared);
        Assert.Equal(1, f.RefCount);
        Assert.Equal(new[] { "modHerm" }, f.Owners);
    }

    [Fact]
    public void Same_folder_in_mod_and_dlc_domains_are_separate()
    {
        var claims = new FolderClaim[]
        {
            new("modHerm", TargetDomain.Mods, "MagicSpellsRevamped"),
            new("dlcHerm", TargetDomain.Dlc, "MagicSpellsRevamped"),
        };

        var result = SharedResourceResolver.Resolve(claims);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(1, r.RefCount));
        Assert.Equal(TargetDomain.Mods, result.Single(r => r.Domain == TargetDomain.Mods).Domain);
    }

    [Fact]
    public void Duplicate_claims_from_same_component_count_once()
    {
        var claims = new FolderClaim[]
        {
            new("modHerm", TargetDomain.Mods, "mod_EH_RiderUtil"),
            new("modHerm", TargetDomain.Mods, "mod_EH_RiderUtil"),
            new("modGry", TargetDomain.Mods, "mod_EH_RiderUtil"),
        };

        var f = Assert.Single(SharedResourceResolver.Resolve(claims));
        Assert.Equal(2, f.RefCount);
        Assert.Equal(new[] { "modGry", "modHerm" }, f.Owners);
    }

    [Fact]
    public void Folder_normalization_is_case_insensitive()
    {
        var claims = new FolderClaim[]
        {
            new("modHerm", TargetDomain.Mods, "MOD_EH_RiderUtil"),
            new("modGry", TargetDomain.Mods, "mod_eh_riderutil"),
        };

        var result = SharedResourceResolver.Resolve(claims);
        Assert.Equal(2, Assert.Single(result).RefCount);
    }

    [Fact]
    public void Null_claims_yield_empty_result()
    {
        Assert.Empty(SharedResourceResolver.Resolve(null!));
    }
}