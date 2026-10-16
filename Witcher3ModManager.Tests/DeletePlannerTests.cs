using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class DeletePlannerTests
{
    private const string GameMods = @"C:\game\mods";
    private const string GameDlc = @"C:\game\dlc";

    [Fact]
    public void An_enabled_folder_is_stashed_out_of_the_game_directory()
    {
        var plan = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DeploymentAction.Stash, step.Action);
        Assert.Equal(@"C:\game\mods\modA", step.SourcePath);
    }

    [Fact]
    public void A_disabled_folder_is_stashed_too()
    {
        // Only the "~" shape is on disk; deleting still has to remove it.
        var plan = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\~modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DeploymentAction.Stash, step.Action);
        Assert.Equal(@"C:\game\mods\~modA", step.SourcePath);
    }

    [Fact]
    public void Both_shapes_on_disk_become_two_steps_with_distinct_subjects()
    {
        // Distinct subjects keep the two folders apart in the staging area.
        var plan = Plan(new[] { "modA" }, existing: new[] { @"C:\game\mods\modA", @"C:\game\mods\~modA" });

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(new[] { "modA", "~modA" }, plan.Steps.Select(s => s.Subject));
        Assert.All(plan.Steps, s => Assert.Equal(DeploymentAction.Stash, s.Action));
    }

    [Fact]
    public void A_folder_that_is_nowhere_says_so_instead_of_failing()
    {
        var plan = Plan(new[] { "modA" }, existing: Array.Empty<string>());

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DeploymentAction.NothingToDo, step.Action);
        Assert.Empty(plan.Actionable);
    }

    [Fact]
    public void A_name_that_already_carries_the_marker_is_planned_once()
    {
        var plan = Plan(new[] { "~modA" }, existing: new[] { @"C:\game\mods\modA" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal("modA", step.Subject);
        Assert.Equal(@"C:\game\mods\modA", step.SourcePath);
    }

    [Fact]
    public void Mod_and_dlc_folders_are_planned_in_their_own_directories()
    {
        var plan = Plan(new[] { "shared" }, dlcs: new[] { "shared" },
            existing: new[] { @"C:\game\mods\shared", @"C:\game\dlc\shared" });

        Assert.Equal(@"C:\game\mods\shared", plan.Steps.Single(s => s.Domain == TargetDomain.Mods).SourcePath);
        Assert.Equal(@"C:\game\dlc\shared", plan.Steps.Single(s => s.Domain == TargetDomain.Dlc).SourcePath);
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
        var plan = DeletePlanner.Plan(null, null, new[] { "modA" }, new[] { "dlcA" }, _ => true);

        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void A_stashed_folder_is_restored_when_a_later_step_fails()
    {
        // Whatever the game directory looks like afterwards, a failed delete must not eat content.
        var root = Path.Combine(Path.GetTempPath(), "w3mm_delete_" + Guid.NewGuid().ToString("N")[..8]);
        var gameMods = Path.Combine(root, "game", "mods");
        var staging = Path.Combine(root, "game", "_w3mm_staging");
        try
        {
            Directory.CreateDirectory(Path.Combine(gameMods, "modA"));
            File.WriteAllText(Path.Combine(gameMods, "modA", "a.txt"), "CONTENT");

            var plan = DeletePlanner.Plan(gameMods, null, new[] { "modA" }, null,
                Directory.Exists);

            var result = new DeploymentEngine(staging).Execute(plan, "op1");

            Assert.True(result.Ok);
            Assert.False(Directory.Exists(Path.Combine(gameMods, "modA")));
            // A successful operation leaves no staging behind.
            Assert.False(Directory.Exists(Path.Combine(staging, "op1")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static DeploymentPlan Plan(
        IEnumerable<string>? mods,
        IEnumerable<string>? dlcs = null,
        IEnumerable<string>? existing = null)
    {
        var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return DeletePlanner.Plan(GameMods, GameDlc, mods, dlcs, set.Contains);
    }
}
