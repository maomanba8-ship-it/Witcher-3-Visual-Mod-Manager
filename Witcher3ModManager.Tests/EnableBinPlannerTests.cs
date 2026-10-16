using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class EnableBinPlannerTests
{
    private const string GameBin = @"C:\game\bin";
    private const string StorageBin = @"C:\data\bin\pkg";
    private const string InputXml = "config/r4game/user_config_matrix/pc/input.xml";

    [Fact]
    public void A_file_with_a_storage_copy_becomes_one_copy_step()
    {
        var plan = Plan(new[] { InputXml }, existing: new[] { StorageBin + @"\config\r4game\user_config_matrix\pc\input.xml" });

        var step = Assert.Single(plan.Steps);
        Assert.Equal(TargetDomain.Bin, step.Domain);
        Assert.Equal(InputXml, step.Subject);
        Assert.Equal(DeploymentAction.Copy, step.Action);
        Assert.Equal(@"C:\data\bin\pkg\config\r4game\user_config_matrix\pc\input.xml", step.SourcePath);
        Assert.Equal(@"C:\game\bin\config\r4game\user_config_matrix\pc\input.xml", step.DestinationPath);
    }

    [Fact]
    public void A_file_with_nothing_to_copy_from_is_recorded_as_a_skip()
    {
        var plan = Plan(new[] { InputXml }, existing: Array.Empty<string>());

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DeploymentAction.NothingToDo, step.Action);
        Assert.Empty(plan.Actionable);

        var note = Assert.Single(OperationOutcome.NoSourceNotes(plan));
        Assert.Equal(OperationNoteKind.NoSource, note.Kind);
        Assert.Equal(TargetDomain.Bin, note.Domain);
        Assert.Equal(InputXml, note.Subject);
    }

    [Fact]
    public void Paths_from_a_record_are_normalised_whichever_separator_they_use()
    {
        var plan = Plan(new[] { @"config\r4game\user_config_matrix\pc\input.xml" },
            existing: new[] { StorageBin + @"\config\r4game\user_config_matrix\pc\input.xml" });

        // The subject is the same string the ledger keys its records by.
        Assert.Equal(InputXml, Assert.Single(plan.Steps).Subject);
    }

    [Fact]
    public void The_same_file_listed_twice_is_planned_once()
    {
        var plan = Plan(new[] { InputXml, InputXml, @"config\r4game\user_config_matrix\pc\input.xml", "", "  " },
            existing: new[] { StorageBin + @"\config\r4game\user_config_matrix\pc\input.xml" });

        Assert.Single(plan.Steps);
    }

    [Fact]
    public void Without_a_game_or_storage_root_nothing_is_planned()
    {
        Assert.Empty(EnableBinPlanner.Plan(null, StorageBin, new[] { InputXml }, _ => true).Steps);
        Assert.Empty(EnableBinPlanner.Plan(GameBin, null, new[] { InputXml }, _ => true).Steps);
        Assert.Empty(EnableBinPlanner.Plan(GameBin, StorageBin, null, _ => true).Steps);
    }

    [Fact]
    public void Plans_are_combined_in_order_so_one_failure_rolls_all_of_them_back()
    {
        var binPlan = Plan(new[] { InputXml }, existing: new[] { StorageBin + @"\config\r4game\user_config_matrix\pc\input.xml" });
        var folderStep = new DeploymentStep(TargetDomain.Mods, "modA", DeploymentAction.Move, @"C:\game\mods\modA", @"C:\game\mods\modA2");

        var combined = DeploymentPlan.Combined(binPlan, new DeploymentPlan(new[] { folderStep }), null);

        Assert.Equal(2, combined.Steps.Count);
        Assert.Equal(TargetDomain.Bin, combined.Steps[0].Domain);
        Assert.Equal(TargetDomain.Mods, combined.Steps[1].Domain);
    }

    private static DeploymentPlan Plan(IEnumerable<string>? paths, IEnumerable<string>? existing)
    {
        var set = new HashSet<string>(existing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return EnableBinPlanner.Plan(GameBin, StorageBin, paths, set.Contains);
    }
}
