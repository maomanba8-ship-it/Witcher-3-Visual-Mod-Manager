using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class DisablePlannerTests
{
    private const string GameMods = @"C:\game\mods";
    private const string GameDlc = @"C:\game\dlc";

    [Fact]
    public void An_enabled_folder_is_renamed_with_the_disabled_marker()
    {
        var plan = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DeploymentAction.Move, step.Action);
        Assert.Equal(@"C:\game\mods\modA", step.SourcePath);
        Assert.Equal(@"C:\game\mods\~modA", step.DestinationPath);
    }

    [Fact]
    public void A_folder_that_is_not_there_needs_nothing()
    {
        var plan = Plan(new[] { "modA" }, existing: Array.Empty<string>());

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DeploymentAction.NothingToDo, step.Action);
        Assert.Empty(plan.Actionable);
    }

    [Fact]
    public void An_already_disabled_folder_is_left_alone()
    {
        // The enabled name is absent, so there is nothing to rename.
        var plan = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\~modA" });

        Assert.Equal(DeploymentAction.NothingToDo, Assert.Single(plan.Steps).Action);
    }

    [Fact]
    public void A_name_that_already_carries_the_marker_is_planned_once()
    {
        var plan = Plan(new[] { "~modA" }, existing: new[] { @"C:\game\mods\modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal("modA", step.Subject);
        Assert.Equal(@"C:\game\mods\modA", step.SourcePath);
        Assert.Equal(@"C:\game\mods\~modA", step.DestinationPath);
    }

    [Fact]
    public void Mod_and_dlc_folders_are_planned_in_their_own_directories()
    {
        var plan = Plan(new[] { "shared" }, dlcs: new[] { "shared" },
            existing: new[] { @"C:\game\mods\shared", @"C:\game\dlc\shared" });

        var mod = plan.Steps.Single(s => s.Domain == TargetDomain.Mods);
        var dlc = plan.Steps.Single(s => s.Domain == TargetDomain.Dlc);
        Assert.Equal(@"C:\game\mods\~shared", mod.DestinationPath);
        Assert.Equal(@"C:\game\dlc\~shared", dlc.DestinationPath);
    }

    [Fact]
    public void Blank_and_duplicate_names_are_ignored()
    {
        var plan = Plan(new[] { "modA", "MODA", "", "   " }, existing: new[] { @"C:\game\mods\modA" });

        Assert.Single(plan.Steps);
    }

    [Fact]
    public void Without_a_game_root_nothing_is_planned()
    {
        var plan = DisablePlanner.Plan(null, null, new[] { "modA" }, new[] { "dlcA" }, _ => true);

        Assert.Empty(plan.Steps);
    }

    private static DeploymentPlan Plan(
        IEnumerable<string>? mods,
        IEnumerable<string>? dlcs = null,
        IEnumerable<string>? existing = null)
    {
        var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return DisablePlanner.Plan(GameMods, GameDlc, mods, dlcs, set.Contains);
    }
}
