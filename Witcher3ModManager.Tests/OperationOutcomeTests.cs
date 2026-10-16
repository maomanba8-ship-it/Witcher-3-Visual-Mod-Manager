using System;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class OperationOutcomeTests
{
    [Fact]
    public void A_successful_deployment_is_ok_and_has_no_problems()
    {
        var result = new DeploymentResult(true,
            new[]
            {
                Done(TargetDomain.Mods, "modA", @"C:\game\mods\modA"),
                new DeploymentStepResult(TargetDomain.Dlc, "dlcA", @"C:\game\dlc\dlcA",
                    DeploymentAction.NothingToDo, DeploymentStepOutcome.Skipped)
            },
            null, Array.Empty<string>());

        var outcome = OperationOutcome.From(OperationKind.Enable, result);

        Assert.True(outcome.Ok);
        Assert.False(outcome.RolledBack);
        Assert.Empty(outcome.Problems);
        // The skipped step is still there for a report to explain, it is just not a problem.
        Assert.Equal(2, outcome.Steps.Count);
    }

    [Fact]
    public void A_failed_step_names_the_component_it_died_on()
    {
        var result = new DeploymentResult(false,
            new[]
            {
                new DeploymentStepResult(TargetDomain.Mods, "modA", @"C:\game\mods\modA",
                    DeploymentAction.Move, DeploymentStepOutcome.Failed, Detail: "file is in use")
            },
            "Move failed for C:\\game\\mods\\modA: file is in use",
            Array.Empty<string>());

        var outcome = OperationOutcome.From(OperationKind.Enable, result);

        var note = Assert.Single(outcome.Problems);
        Assert.Equal(OperationNoteKind.StepFailed, note.Kind);
        Assert.Equal(TargetDomain.Mods, note.Domain);
        Assert.Equal("modA", note.Subject);
        Assert.Equal("file is in use", note.Detail);
    }

    [Fact]
    public void A_failure_with_nothing_left_undone_counts_as_rolled_back()
    {
        var clean = OperationOutcome.From(OperationKind.Delete, FailedResult(Array.Empty<string>()));
        var messy = OperationOutcome.From(OperationKind.Delete,
            FailedResult(new[] { "put back C:\\game\\mods\\modA: access denied" }));

        Assert.True(clean.RolledBack);
        // Something could not be undone, so claiming the previous state was restored would be wrong.
        Assert.False(messy.RolledBack);
        Assert.Contains(messy.Problems, n => n.Kind == OperationNoteKind.RollbackProblem);
    }

    [Fact]
    public void Folders_an_enable_had_no_source_for_become_notes()
    {
        var plan = EnablePlanner.Plan(@"C:\game\mods", @"C:\game\dlc", @"C:\storage",
            new[] { "modA", "modB" }, new[] { "dlcA" },
            path => path == @"C:\game\mods\modA");   // modA is in place, the other two are nowhere

        var notes = OperationOutcome.NoSourceNotes(plan);

        Assert.Equal(new[] { "modB" }, notes.Where(n => n.Domain == TargetDomain.Mods).Select(n => n.Subject));
        Assert.Equal(new[] { "dlcA" }, notes.Where(n => n.Domain == TargetDomain.Dlc).Select(n => n.Subject));
        Assert.All(notes, n => Assert.Equal(OperationNoteKind.NoSource, n.Kind));
    }

    [Fact]
    public void There_are_no_source_notes_without_a_plan()
    {
        Assert.Empty(OperationOutcome.NoSourceNotes((EnablePlan?)null));
        Assert.Empty(OperationOutcome.NoSourceNotes((DeploymentPlan?)null));
    }

    [Fact]
    public void Paths_that_could_not_be_removed_are_reported_individually()
    {
        var outcome = OperationOutcome.Failed(OperationKind.Delete, "one or more paths could not be deleted",
            new[] { @"C:\storage\modA" });

        var note = Assert.Single(outcome.Problems);
        Assert.Equal(OperationNoteKind.PathNotRemoved, note.Kind);
        Assert.Equal(@"C:\storage\modA", note.Detail);
    }

    private static DeploymentStepResult Done(TargetDomain domain, string subject, string target) =>
        new(domain, subject, target, DeploymentAction.Move, DeploymentStepOutcome.Done);

    private static DeploymentResult FailedResult(System.Collections.Generic.IReadOnlyList<string> rollbackProblems) =>
        new(false,
            new[]
            {
                new DeploymentStepResult(TargetDomain.Mods, "modA", @"C:\game\mods\modA",
                    DeploymentAction.Stash, DeploymentStepOutcome.Failed, Detail: "access denied")
            },
            "access denied",
            rollbackProblems);
}
