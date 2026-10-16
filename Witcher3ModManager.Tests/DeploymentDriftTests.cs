using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class DeploymentDriftTests
{
    [Fact]
    public void Everything_still_in_place_is_not_drift()
    {
        var drifts = DeploymentDrift.Find(Index(), _ => TargetPresence.Present);

        Assert.Empty(drifts);
    }

    [Fact]
    public void A_target_that_is_gone_is_drift()
    {
        var drifts = DeploymentDrift.Find(Index(), target =>
            target.Target.RelativePath == "mods/modA" ? TargetPresence.Missing : TargetPresence.Present);

        var drift = Assert.Single(drifts);
        Assert.Equal(DriftKind.Missing, drift.Kind);
        Assert.Equal("pkg1", drift.PackageId);
        Assert.Equal("Mod One", drift.DisplayName);
        Assert.Equal("mods/modA", drift.RelativePath);
        Assert.Equal(TargetDomain.Mods, drift.Domain);
    }

    [Fact]
    public void A_target_whose_content_changed_is_drift()
    {
        var drifts = DeploymentDrift.Find(Index(), _ => TargetPresence.Modified);

        Assert.Equal(2, drifts.Count);
        Assert.All(drifts, d => Assert.Equal(DriftKind.Modified, d.Kind));
    }

    [Fact]
    public void The_probe_is_told_which_component_owns_the_target()
    {
        var seen = new List<DeployedTarget>();

        DeploymentDrift.Find(Index(), target =>
        {
            seen.Add(target);
            return TargetPresence.Present;
        });

        var bin = Assert.Single(seen, s => s.Domain == TargetDomain.Bin);
        Assert.Equal(@"C:\data\bin\pkg1", bin.StorageFolder);
        Assert.Equal("bin/config/a.xml", bin.Target.RelativePath);
    }

    [Fact]
    public void Drifts_from_several_packages_are_all_reported()
    {
        var index = LibraryIndexUpdater.WithPackage(Index(), "pkg2", "Mod Two", new[]
        {
            new ComponentRecord("c3", TargetDomain.Mods, "modTwo",
                new[] { new TargetRecord("mods/modTwo", TargetKind.Folder, "hash", DateTime.UnixEpoch) })
        });

        var drifts = DeploymentDrift.Find(index, _ => TargetPresence.Missing);

        Assert.Equal(3, drifts.Count);
        Assert.Equal(2, drifts.Select(d => d.PackageId).Distinct().Count());
    }

    [Fact]
    public void Without_an_index_or_a_probe_there_is_nothing_to_report()
    {
        Assert.Empty(DeploymentDrift.Find(null, _ => TargetPresence.Missing));
        Assert.Empty(DeploymentDrift.Find(Index(), null));
        Assert.Empty(DeploymentDrift.Find(LibraryIndex.Empty, _ => TargetPresence.Missing));
    }

    private static LibraryIndex Index() =>
        LibraryIndexUpdater.WithPackage(LibraryIndex.Empty, "pkg1", "Mod One", new[]
        {
            new ComponentRecord("c1", TargetDomain.Mods, "modOne",
                new[] { new TargetRecord("mods/modA", TargetKind.Folder, "hash", DateTime.UnixEpoch) }),
            new ComponentRecord("c2", TargetDomain.Bin, @"C:\data\bin\pkg1",
                new[] { new TargetRecord("bin/config/a.xml", TargetKind.File, "hash", DateTime.UnixEpoch) })
        });
}
