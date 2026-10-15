using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class DeploymentStateEvaluatorTests
{
    private static readonly TargetRecord[] TwoTargets =
    {
        new(@"mods\modA", TargetKind.Folder, "hashA", DateTime.UnixEpoch),
        new(@"mods\modB", TargetKind.Folder, "hashB", DateTime.UnixEpoch)
    };

    [Fact]
    public void A_package_the_index_never_saw_is_unknown_rather_than_assumed()
    {
        var state = DeploymentStateEvaluator.Evaluate(
            hasIndexRecord: false,
            isEnabled: true,
            indexedTargets: TwoTargets,
            presence: _ => TargetPresence.Present);

        Assert.Equal(DeploymentState.UnknownOwnership, state);
    }

    [Fact]
    public void Nothing_recorded_as_deployed_is_not_deployed()
    {
        Assert.Equal(DeploymentState.NotDeployed, Evaluate(isEnabled: false, targets: Array.Empty<TargetRecord>()));
    }

    [Fact]
    public void An_enabled_package_with_nothing_recorded_cannot_be_honoured()
    {
        Assert.Equal(DeploymentState.NeedsRepair, Evaluate(isEnabled: true, targets: Array.Empty<TargetRecord>()));
    }

    [Fact]
    public void Everything_present_and_matching_is_deployed()
    {
        Assert.Equal(DeploymentState.Deployed, Evaluate(isEnabled: true, targets: TwoTargets, presence: _ => TargetPresence.Present));
    }

    [Fact]
    public void Some_there_and_some_gone_is_partially_deployed()
    {
        var state = Evaluate(
            isEnabled: true,
            targets: TwoTargets,
            presence: t => t.RelativePath.EndsWith("modA") ? TargetPresence.Present : TargetPresence.Missing);

        Assert.Equal(DeploymentState.PartiallyDeployed, state);
    }

    [Fact]
    public void Enabled_but_nothing_on_disk_is_the_classic_mismatch()
    {
        Assert.Equal(
            DeploymentState.NeedsRepair,
            Evaluate(isEnabled: true, targets: TwoTargets, presence: _ => TargetPresence.Missing));
    }

    [Fact]
    public void Disabled_and_nothing_on_disk_is_simply_not_deployed()
    {
        Assert.Equal(
            DeploymentState.NotDeployed,
            Evaluate(isEnabled: false, targets: TwoTargets, presence: _ => TargetPresence.Missing));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Content_that_changed_underneath_needs_a_person(bool isEnabled)
    {
        // Modified beats the other counts in both directions: whether it is now "someone else's"
        // file has to be decided by the user, not inferred from an enabled flag.
        var state = Evaluate(
            isEnabled: isEnabled,
            targets: TwoTargets,
            presence: t => t.RelativePath.EndsWith("modA") ? TargetPresence.Modified : TargetPresence.Present);

        Assert.Equal(DeploymentState.NeedsRepair, state);
    }

    [Fact]
    public void Without_a_way_to_look_at_disk_the_answer_is_unknown()
    {
        Assert.Equal(
            DeploymentState.UnknownOwnership,
            DeploymentStateEvaluator.Evaluate(true, true, TwoTargets, presence: null));
    }

    private static DeploymentState Evaluate(
        bool isEnabled,
        IReadOnlyList<TargetRecord> targets,
        Func<TargetRecord, TargetPresence>? presence = null) =>
        DeploymentStateEvaluator.Evaluate(true, isEnabled, targets, presence ?? (_ => TargetPresence.Missing));
}
