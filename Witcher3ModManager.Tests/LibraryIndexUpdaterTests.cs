using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class LibraryIndexUpdaterTests
{
    [Fact]
    public void Recording_a_package_adds_it_to_an_empty_index()
    {
        var index = LibraryIndexUpdater.WithPackage(
            null, "pkg1", "Mod One", new[] { Component("c1", Target(@"mods\modOne")) });

        var package = Assert.Single(index.Packages);
        Assert.Equal("pkg1", package.PackageId);
        Assert.Equal("Mod One", package.DisplayName);
        Assert.Equal(@"mods\modOne", Assert.Single(Assert.Single(package.Components).Targets).RelativePath);
    }

    [Fact]
    public void Recording_the_same_package_again_replaces_it_wholesale()
    {
        var first = LibraryIndexUpdater.WithPackage(null, "pkg1", "Old name", new[]
        {
            Component("c1", Target(@"mods\staleFolder"))
        });

        var second = LibraryIndexUpdater.WithPackage(first, "pkg1", "New name", new[]
        {
            Component("c1", Target(@"mods\currentFolder"))
        });

        var package = Assert.Single(second.Packages);
        Assert.Equal("New name", package.DisplayName);
        // The stale target is gone: a leftover claim would make a later rollback touch the
        // wrong file.
        Assert.Equal(@"mods\currentFolder", Assert.Single(Assert.Single(package.Components).Targets).RelativePath);
    }

    [Fact]
    public void Recorded_packages_are_kept_in_a_stable_order()
    {
        var index = LibraryIndexUpdater.WithPackage(null, "pkgB", "B", Array.Empty<ComponentRecord>());
        index = LibraryIndexUpdater.WithPackage(index, "pkgA", "A", Array.Empty<ComponentRecord>());
        index = LibraryIndexUpdater.WithPackage(index, "pkgC", "C", Array.Empty<ComponentRecord>());

        Assert.Equal(new[] { "pkgA", "pkgB", "pkgC" }, index.Packages.Select(p => p.PackageId).ToArray());
    }

    [Fact]
    public void Forgetting_a_package_removes_only_that_one()
    {
        var index = LibraryIndexUpdater.WithPackage(null, "pkgA", "A", Array.Empty<ComponentRecord>());
        index = LibraryIndexUpdater.WithPackage(index, "pkgB", "B", Array.Empty<ComponentRecord>());

        var after = LibraryIndexUpdater.WithoutPackage(index, "pkgA");

        Assert.Equal("pkgB", Assert.Single(after.Packages).PackageId);
        Assert.Null(after.OwnerOf("mods/anything"));
    }

    [Fact]
    public void Forgetting_something_that_is_not_there_changes_nothing()
    {
        var index = LibraryIndexUpdater.WithPackage(null, "pkgA", "A", Array.Empty<ComponentRecord>());

        Assert.Same(index, LibraryIndexUpdater.WithoutPackage(index, "pkgMissing"));
        Assert.Same(index, LibraryIndexUpdater.WithPackage(index, "", "ignored", Array.Empty<ComponentRecord>()));
    }

    [Fact]
    public void Declaring_components_again_keeps_the_targets_already_deployed()
    {
        // Deployed first, so the component owns a target in the game directory.
        var deployed = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One", new[]
        {
            Component("c1", Target(@"mods\modOne"))
        });

        // Then re-imported: the import declares the same component but has deployed nothing, and
        // an import does not undeploy. Forgetting the target here would lose track of files that
        // are still sitting in the game directory.
        var afterImport = LibraryIndexUpdater.WithDeclaredComponents(deployed, "pkg1", "Mod One", new[]
        {
            Component("c1")
        });

        var component = Assert.Single(Assert.Single(afterImport.Packages).Components);
        Assert.Equal(@"mods\modOne", Assert.Single(component.Targets).RelativePath);
    }

    [Fact]
    public void A_fresh_deployment_replaces_the_recorded_targets()
    {
        var stale = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One", new[]
        {
            Component("c1", Target(@"mods\oldFolder"))
        });

        var redeployed = LibraryIndexUpdater.WithDeclaredComponents(stale, "pkg1", "Mod One", new[]
        {
            Component("c1", Target(@"mods\newFolder"))
        });

        var component = Assert.Single(Assert.Single(redeployed.Packages).Components);
        Assert.Equal(@"mods\newFolder", Assert.Single(component.Targets).RelativePath);
    }

    [Fact]
    public void A_component_that_is_no_longer_declared_loses_its_claims()
    {
        var before = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One", new[]
        {
            Component("c1", Target(@"mods\modOne"))
        });

        var after = LibraryIndexUpdater.WithDeclaredComponents(before, "pkg1", "Mod One", Array.Empty<ComponentRecord>());

        Assert.Empty(Assert.Single(after.Packages).Components);
        Assert.Null(after.OwnerOf("mods/modone"));
    }

    [Fact]
    public void Ownership_can_be_asked_through_the_index_after_recording()
    {
        var index = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One", new[]
        {
            Component("c1", Target(@"mods\modOne"), Target(@"bin\config\a.xml", TargetKind.File))
        });

        Assert.Equal("c1", index.OwnerOf("mods/modone")!.ComponentId);
        Assert.Equal("c1", index.OwnerOf(@"BIN\config\a.xml")!.ComponentId);
        Assert.Null(index.OwnerOf("mods/someoneElse"));
    }

    [Fact]
    public void Clearing_bin_targets_leaves_the_other_components_alone()
    {
        var index = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One", new[]
        {
            Component("c1", Target(@"mods\modOne")),
            BinComponent("c2", Target(@"bin\config\a.xml", TargetKind.File))
        });

        var updated = LibraryIndexUpdater.WithoutBinTargets(index, "pkg1");

        var components = Assert.Single(updated.Packages).Components;
        Assert.Single(components.Single(c => c.Domain == TargetDomain.Mods).Targets);
        // The component stays: the record still declares bin files, it just deploys none of them.
        Assert.Empty(components.Single(c => c.Domain == TargetDomain.Bin).Targets);
        Assert.Null(updated.OwnerOf("bin/config/a.xml"));
    }

    [Fact]
    public void Clearing_bin_targets_keeps_the_other_packages()
    {
        var index = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One",
            new[] { BinComponent("c1", Target(@"bin\config\a.xml", TargetKind.File)) });
        index = LibraryIndexUpdater.WithPackage(index, "pkg2", "Mod Two",
            new[] { BinComponent("c2", Target(@"bin\config\b.xml", TargetKind.File)) });

        var updated = LibraryIndexUpdater.WithoutBinTargets(index, "pkg1");

        Assert.Null(updated.OwnerOf("bin/config/a.xml"));
        Assert.Equal("c2", updated.OwnerOf("bin/config/b.xml")!.ComponentId);
    }

    [Fact]
    public void Clearing_bin_targets_for_an_unknown_package_changes_nothing()
    {
        var index = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One",
            new[] { BinComponent("c1", Target(@"bin\config\a.xml", TargetKind.File)) });

        Assert.Equal(index, LibraryIndexUpdater.WithoutBinTargets(index, "not-here"));
        Assert.Equal(index, LibraryIndexUpdater.WithoutBinTargets(index, ""));
    }

    private static ComponentRecord Component(string id, params TargetRecord[] targets) =>
        new(id, TargetDomain.Mods, "modOne", targets);

    private static ComponentRecord BinComponent(string id, params TargetRecord[] targets) =>
        new(id, TargetDomain.Bin, @"C:\data\bin\pkg1", targets);

    private static TargetRecord Target(string path, TargetKind kind = TargetKind.Folder) =>
        new(path, kind, "hash", DateTime.UnixEpoch);
}
