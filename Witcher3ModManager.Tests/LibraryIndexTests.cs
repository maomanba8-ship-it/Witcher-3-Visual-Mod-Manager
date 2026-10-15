using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class LibraryIndexTests
{
    [Fact]
    public void A_component_id_is_derived_from_the_package_the_domain_and_the_folder()
    {
        var id = ComponentRecord.MakeId("pkg1", TargetDomain.Mods, "modFoo");

        Assert.Equal(id, ComponentRecord.MakeId("pkg1", TargetDomain.Mods, "modFoo"));
        Assert.Equal(id, ComponentRecord.MakeId("pkg1", TargetDomain.Mods, "MODFOO"));   // folder case
        Assert.Equal(id, ComponentRecord.MakeId("pkg1", TargetDomain.Mods, @"modFoo\")); // stray separator

        Assert.NotEqual(id, ComponentRecord.MakeId("pkg1", TargetDomain.Dlc, "modFoo"));
        Assert.NotEqual(id, ComponentRecord.MakeId("pkg2", TargetDomain.Mods, "modFoo"));
    }

    [Fact]
    public void Records_are_found_regardless_of_case()
    {
        var index = IndexWith(Package("PKG1", Component("PKG1:Mods:modfoo", TargetDomain.Mods, "modFoo")));

        Assert.NotNull(index.FindPackage("pkg1"));
        Assert.NotNull(index.FindComponent("pkg1:mods:MODFOO"));
        Assert.Null(index.FindPackage("nope"));
        Assert.Null(index.FindComponent(null));
    }

    [Fact]
    public void Ownership_is_answered_ignoring_separators_and_case()
    {
        var index = IndexWith(Package("pkg1",
            new ComponentRecord("c1", TargetDomain.Mods, "modFoo", new[]
            {
                new TargetRecord(@"mods\modFoo", TargetKind.Folder, "hash", DateTime.UnixEpoch)
            })));

        Assert.Equal("c1", index.OwnerOf("mods/modfoo")!.ComponentId);
        Assert.Equal("c1", index.OwnerOf(@"MODS\ModFoo")!.ComponentId);
        Assert.Null(index.OwnerOf("mods/modOther"));
        Assert.Null(index.OwnerOf(""));
        Assert.Null(index.OwnerOf(null));
    }

    [Fact]
    public void The_index_survives_a_json_round_trip()
    {
        var original = IndexWith(Package("pkg1",
            new ComponentRecord("c1", TargetDomain.Dlc, "dlcY", new[]
            {
                new TargetRecord(@"dlc\dlcY", TargetKind.Folder, "hashY", new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc))
            })));

        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<LibraryIndex>(json);

        Assert.NotNull(restored);
        Assert.Equal(LibraryIndex.CurrentVersion, restored!.Version);
        var package = Assert.Single(restored.Packages);
        Assert.Equal("pkg1", package.PackageId);
        var component = Assert.Single(package.Components);
        Assert.Equal("c1", component.ComponentId);
        Assert.Equal(TargetDomain.Dlc, component.Domain);
        Assert.Equal("dlcY", component.StorageFolder);
        // Compared field by field: record equality compares collection members by reference, so
        // two structurally identical indexes are not `==` to each other.
        var target = Assert.Single(component.Targets);
        Assert.Equal(@"dlc\dlcY", target.RelativePath);
        Assert.Equal("hashY", target.WrittenHash);
        Assert.Equal(TargetKind.Folder, target.Kind);
    }

    [Fact]
    public void An_empty_index_claims_nothing()
    {
        Assert.Empty(LibraryIndex.Empty.Packages);
        Assert.Equal(LibraryIndex.CurrentVersion, LibraryIndex.Empty.Version);
        Assert.Null(LibraryIndex.Empty.OwnerOf("mods/modFoo"));
        Assert.Null(LibraryIndex.Empty.FindPackage("pkg1"));
    }

    [Fact]
    public void A_package_without_components_does_not_break_lookups()
    {
        var index = IndexWith(new PackageRecord("pkg1", "Empty", null!));

        Assert.Empty(index.FindPackage("pkg1")!.Components ?? Array.Empty<ComponentRecord>());
        Assert.Null(index.OwnerOf("mods/modFoo"));
        Assert.NotNull(index.FindPackage("pkg1"));
    }

    private static LibraryIndex IndexWith(params PackageRecord[] packages) =>
        new(LibraryIndex.CurrentVersion, packages);

    private static PackageRecord Package(string id, params ComponentRecord[] components) =>
        new(id, id, components);

    private static ComponentRecord Component(string id, TargetDomain domain, string folder, params TargetRecord[] targets) =>
        new(id, domain, folder, targets);
}
