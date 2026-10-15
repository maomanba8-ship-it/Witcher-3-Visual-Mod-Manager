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

    private static ComponentRecord Component(string id, params TargetRecord[] targets) =>
        new(id, TargetDomain.Mods, "modOne", targets);

    private static TargetRecord Target(string path, TargetKind kind = TargetKind.Folder) =>
        new(path, kind, "hash", DateTime.UnixEpoch);
}
