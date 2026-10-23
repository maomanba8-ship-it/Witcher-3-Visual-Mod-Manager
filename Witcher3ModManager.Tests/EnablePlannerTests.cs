using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class EnablePlannerTests
{
    private const string GameMods = @"C:\game\mods";
    private const string GameDlc = @"C:\game\dlc";
    private const string Storage = @"C:\data\mods";

    [Fact]
    public void A_disabled_folder_in_the_game_directory_is_restored()
    {
        var plan = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\~modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EnableAction.RestoreDisabledFolder, step.Action);
        Assert.Equal(@"C:\game\mods\modA", step.TargetPath);
        Assert.Equal(@"C:\game\mods\~modA", step.DisabledPath);
        Assert.False(step.TargetOccupied);
        Assert.Equal(step, Assert.Single(plan.Work));
    }

    [Fact]
    public void A_restore_that_lands_on_an_occupied_target_says_so()
    {
        // Both the enabled name and the disabled marker exist: the enabled one will have to be
        // dealt with, which is the case that used to delete it outright.
        var plan = Plan(new[] { "modA" },
            existing: new[] { @"C:\game\mods\modA", @"C:\game\mods\~modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EnableAction.RestoreDisabledFolder, step.Action);
        Assert.True(step.TargetOccupied);
        Assert.True(plan.ReplacesOccupiedTarget);
    }

    [Fact]
    public void A_folder_only_in_storage_is_copied_in()
    {
        var plan = Plan(new[] { "modA" }, existing: new[] { @"C:\data\mods\modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EnableAction.CopyFromStorage, step.Action);
        Assert.Equal(@"C:\data\mods\modA", step.StorageSourcePath);
        Assert.False(step.TargetOccupied);
        Assert.False(plan.ReplacesOccupiedTarget);
    }

    [Fact]
    public void A_folder_already_in_the_game_directory_needs_nothing()
    {
        var plan = Plan(new[] { "modA" },
            existing: new[] { @"C:\game\mods\modA", @"C:\data\mods\modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EnableAction.AlreadyInPlace, step.Action);
        Assert.Empty(plan.Work);
    }

    [Fact]
    public void An_empty_folder_in_the_game_directory_is_refilled_from_storage()
    {
        // A failed or interrupted operation can leave the target folder behind with nothing in
        // it. That is not "already in place": treating it as such reported the package as enabled
        // while the game directory held no mod at all.
        var plan = Plan(new[] { "modA" },
            existing: new[] { @"C:\game\mods\modA", @"C:\data\mods\modA" },
            empty: new[] { @"C:\game\mods\modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EnableAction.CopyFromStorage, step.Action);
        Assert.False(step.TargetOccupied);
        Assert.Equal(step, Assert.Single(plan.Work));
    }

    [Fact]
    public void A_folder_with_no_source_is_reported_rather_than_failing()
    {
        var plan = Plan(new[] { "modA", "modB" }, existing: Array.Empty<string>());

        Assert.Equal(2, plan.Steps.Count);
        Assert.All(plan.Steps, s => Assert.Equal(EnableAction.NoSource, s.Action));
        Assert.Equal(2, plan.WithoutSource.Count);
        Assert.Empty(plan.Work);
    }

    [Fact]
    public void Mod_and_dlc_folders_are_planned_in_their_own_directories()
    {
        var plan = Plan(new[] { "sharedName" }, dlcs: new[] { "sharedName" },
            existing: new[] { @"C:\data\mods\sharedName" });

        var mod = plan.Steps.Single(s => s.Domain == TargetDomain.Mods);
        var dlc = plan.Steps.Single(s => s.Domain == TargetDomain.Dlc);
        Assert.Equal(@"C:\game\mods\sharedName", mod.TargetPath);
        Assert.Equal(@"C:\game\dlc\sharedName", dlc.TargetPath);
        Assert.Equal(@"C:\game\dlc\~sharedName", dlc.DisabledPath);
    }

    [Fact]
    public void Blank_and_duplicate_folders_are_ignored()
    {
        var plan = Plan(new[] { "modA", "MODA", "", "   " },
            existing: new[] { @"C:\game\mods\~modA" });

        Assert.Single(plan.Steps);
        Assert.Equal("modA", plan.Steps[0].Folder);
    }

    [Fact]
    public void Without_a_game_root_nothing_is_planned()
    {
        var plan = EnablePlanner.Plan(null, null, Storage, new[] { "modA" }, new[] { "dlcA" }, _ => true);

        Assert.Empty(plan.Steps);
        Assert.Empty(plan.Work);
    }

    [Fact]
    public void Without_a_storage_root_a_copy_cannot_be_planned()
    {
        var plan = EnablePlanner.Plan(GameMods, GameDlc, null, new[] { "modA" }, null, _ => false);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EnableAction.NoSource, step.Action);
        Assert.Null(step.StorageSourcePath);
    }

    [Fact]
    public void The_plan_lists_exactly_the_paths_an_operation_would_touch()
    {
        var plan = Plan(new[] { "modA" }, dlcs: new[] { "dlcB" },
            existing: new[] { @"C:\game\mods\~modA", @"C:\data\mods\dlcB" });

        var mod = plan.Steps.Single(s => s.Domain == TargetDomain.Mods);
        var dlc = plan.Steps.Single(s => s.Domain == TargetDomain.Dlc);
        Assert.Equal(new[] { mod, dlc }, plan.Work);
        Assert.Equal(@"C:\data\mods\dlcB", dlc.StorageSourcePath);
    }

    private static EnablePlan Plan(
        IEnumerable<string>? mods,
        IEnumerable<string>? dlcs = null,
        IEnumerable<string>? existing = null,
        IEnumerable<string>? empty = null)
    {
        var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var emptySet = new HashSet<string>(empty ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return EnablePlanner.Plan(GameMods, GameDlc, Storage, mods, dlcs, set.Contains, emptySet.Contains);
    }
}
