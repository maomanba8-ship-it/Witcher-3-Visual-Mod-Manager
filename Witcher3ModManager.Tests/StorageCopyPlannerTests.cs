using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class StorageCopyPlannerTests
{
    private const string GameMods = @"C:\game\mods";
    private const string Storage = @"C:\data\mods";

    [Fact]
    public void A_folder_only_in_the_game_directory_is_planned()
    {
        var items = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\modA" });

        var item = Assert.Single(items);
        Assert.Equal("modA", item.Folder);
        Assert.Equal(@"C:\game\mods\modA", item.SourcePath);
        Assert.Equal(@"C:\data\mods\modA", item.TargetPath);
    }

    [Fact]
    public void A_disabled_folder_is_a_valid_source_under_its_clean_name()
    {
        var items = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\~modA" });

        var item = Assert.Single(items);
        Assert.Equal(@"C:\game\mods\~modA", item.SourcePath);
        Assert.Equal(@"C:\data\mods\modA", item.TargetPath);
    }

    [Fact]
    public void A_folder_already_in_storage_is_left_alone()
    {
        var items = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\modA", @"C:\data\mods\modA" });

        Assert.Empty(items);
    }

    [Fact]
    public void A_folder_that_is_nowhere_is_skipped()
    {
        Assert.Empty(Plan(new[] { "modA" }, existing: Array.Empty<string>()));
    }

    [Fact]
    public void The_enabled_form_wins_when_both_shapes_exist()
    {
        var items = Plan(new[] { "modA" },
            existing: new[] { @"C:\game\mods\modA", @"C:\game\mods\~modA" });

        Assert.Equal(@"C:\game\mods\modA", Assert.Single(items).SourcePath);
    }

    [Fact]
    public void Names_are_cleaned_and_duplicates_collapse()
    {
        var items = Plan(new[] { "modA", "MODA", "~modA", "", "   " },
            existing: new[] { @"C:\game\mods\modA" });

        Assert.Equal("modA", Assert.Single(items).Folder);
    }

    [Fact]
    public void Without_a_game_or_storage_root_nothing_is_planned()
    {
        Assert.Empty(StorageCopyPlanner.Plan(new[] { "modA" }, null, Storage, _ => true));
        Assert.Empty(StorageCopyPlanner.Plan(new[] { "modA" }, GameMods, null, _ => true));
        Assert.Empty(StorageCopyPlanner.Plan(null, GameMods, Storage, _ => true));
    }

    private static IReadOnlyList<StorageCopyItem> Plan(IEnumerable<string>? folders, IEnumerable<string>? existing)
    {
        var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return StorageCopyPlanner.Plan(folders, GameMods, Storage, set.Contains);
    }
}
