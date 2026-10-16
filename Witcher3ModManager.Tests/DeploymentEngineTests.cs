using System;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class DeploymentEngineTests
{
    [Fact]
    public void A_storage_folder_is_copied_into_the_game_directory()
    {
        using var world = new TestWorld();
        world.WriteStorageFile("modA", "content/a.txt", "AAA");

        var result = world.DeployPlanned("modA");

        Assert.True(result.Ok);
        Assert.Equal("AAA", File.ReadAllText(Path.Combine(world.GameMods, "modA", "content", "a.txt")));
        Assert.Equal(DeploymentStepOutcome.Done, Assert.Single(result.Steps).Outcome);
    }

    [Fact]
    public void A_disabled_folder_is_moved_back_into_place()
    {
        using var world = new TestWorld();
        world.WriteGameDisabledFile("modA", "content/a.txt", "DISABLED");

        var result = world.DeployPlanned("modA");

        Assert.True(result.Ok);
        Assert.True(Directory.Exists(Path.Combine(world.GameMods, "modA")));
        Assert.False(Directory.Exists(Path.Combine(world.GameMods, "~modA")));
        Assert.Equal("DISABLED", File.ReadAllText(Path.Combine(world.GameMods, "modA", "content", "a.txt")));
    }

    [Fact]
    public void An_occupied_destination_is_backed_up_instead_of_deleted()
    {
        using var world = new TestWorld();
        world.WriteGameEnabledFile("modA", "content/a.txt", "OLD");
        world.WriteGameDisabledFile("modA", "content/a.txt", "NEW");

        var result = world.DeployPlanned("modA", operationId: "op1");

        Assert.True(result.Ok);
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(world.GameMods, "modA", "content", "a.txt")));

        // A backup was taken and then cleaned up, because the operation finished. That it really
        // held the previous content is shown by the rollback tests below.
        Assert.NotNull(Assert.Single(result.Backups).BackupPath);
        Assert.False(Directory.Exists(Path.Combine(world.Staging, "op1")));
    }

    [Fact]
    public void Nothing_is_touched_when_a_plan_has_nothing_to_do()
    {
        using var world = new TestWorld();
        world.WriteGameEnabledFile("modA", "content/a.txt", "KEEP");
        world.WriteStorageFile("modA", "content/a.txt", "IGNORED");

        var result = world.DeployPlanned("modA");

        Assert.True(result.Ok);
        // The step is recorded as a skip so a caller can explain why nothing happened.
        Assert.Equal(DeploymentStepOutcome.Skipped, Assert.Single(result.Steps).Outcome);
        Assert.False(Directory.Exists(Path.Combine(world.Staging, "unidentified")));
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(world.GameMods, "modA", "content", "a.txt")));
    }

    [Fact]
    public void A_failure_part_way_through_puts_the_finished_steps_back()
    {
        using var world = new TestWorld();
        world.WriteStorageFile("modA", "content/a.txt", "AAA");

        var result = world.Deploy(new DeploymentPlan(new[]
        {
            new DeploymentStep(TargetDomain.Mods, "modA", DeploymentAction.Copy,
                Path.Combine(world.Storage, "modA"), Path.Combine(world.GameMods, "modA")),
            // Step two points at storage that does not exist, so it has to fail.
            new DeploymentStep(TargetDomain.Mods, "modB", DeploymentAction.Copy,
                Path.Combine(world.Storage, "modB"), Path.Combine(world.GameMods, "modB"))
        }));

        Assert.False(result.Ok);
        Assert.NotNull(result.Failure);
        Assert.Empty(result.RollbackProblems);
        // modA was copied and then undone: the game directory is back to how it started.
        Assert.False(Directory.Exists(Path.Combine(world.GameMods, "modA")));
    }

    [Fact]
    public void A_failure_after_replacing_an_occupied_destination_restores_both_sides()
    {
        using var world = new TestWorld();
        world.WriteGameEnabledFile("modA", "content/a.txt", "OLD");
        world.WriteGameDisabledFile("modA", "content/a.txt", "NEW");

        var result = world.Deploy(new DeploymentPlan(new[]
        {
            new DeploymentStep(TargetDomain.Mods, "modA", DeploymentAction.Move,
                Path.Combine(world.GameMods, "~modA"), Path.Combine(world.GameMods, "modA")),
            new DeploymentStep(TargetDomain.Mods, "modB", DeploymentAction.Copy,
                Path.Combine(world.Storage, "modB"), Path.Combine(world.GameMods, "modB"))
        }), operationId: "op2");

        Assert.False(result.Ok);
        Assert.Empty(result.RollbackProblems);
        // The disabled marker is back, and so is the content that used to sit at the destination.
        Assert.True(Directory.Exists(Path.Combine(world.GameMods, "~modA")));
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(world.GameMods, "~modA", "content", "a.txt")));
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(world.GameMods, "modA", "content", "a.txt")));

        // Rolling back puts the files back, but the staging copy stays: an unfinished operation
        // keeps whatever it would need to be undone again.
        Assert.True(Directory.Exists(Path.Combine(world.Staging, "op2")));
    }

    [Fact]
    public void A_stash_step_moves_things_out_of_the_way_and_can_be_undone()
    {
        using var world = new TestWorld();
        world.WriteGameEnabledFile("modA", "content/a.txt", "CONTENT");

        var result = world.Deploy(new DeploymentPlan(new[]
        {
            new DeploymentStep(TargetDomain.Mods, "modA", DeploymentAction.Stash,
                Path.Combine(world.GameMods, "modA"), ""),
            new DeploymentStep(TargetDomain.Mods, "modB", DeploymentAction.Copy,
                Path.Combine(world.Storage, "modB"), Path.Combine(world.GameMods, "modB"))
        }), operationId: "op3");

        Assert.False(result.Ok);
        Assert.Empty(result.RollbackProblems);
        // The stashed folder is back where it was, with its content.
        Assert.Equal("CONTENT", File.ReadAllText(Path.Combine(world.GameMods, "modA", "content", "a.txt")));
    }

    /// <summary>A throwaway game/storage/staging layout so the engine can be exercised for real.</summary>
    private sealed class TestWorld : IDisposable
    {
        private readonly string _root;

        public TestWorld()
        {
            _root = Path.Combine(Path.GetTempPath(), "w3mm_engine_" + Guid.NewGuid().ToString("N")[..8]);
            GameMods = Path.Combine(_root, "game", "mods");
            GameDlc = Path.Combine(_root, "game", "dlc");
            Storage = Path.Combine(_root, "storage");
            Staging = Path.Combine(_root, "staging");
            Directory.CreateDirectory(GameMods);
            Directory.CreateDirectory(GameDlc);
            Directory.CreateDirectory(Storage);
        }

        public string GameMods { get; }
        public string GameDlc { get; }
        public string Storage { get; }
        public string Staging { get; }

        /// <summary>Plans an enable through the real planner, then executes it.</summary>
        public DeploymentResult DeployPlanned(string folder, string? operationId = null)
        {
            var plan = EnablePlanner.Plan(GameMods, GameDlc, Storage, new[] { folder }, null);
            return Deploy(plan.ToDeploymentPlan(), operationId);
        }

        public DeploymentResult Deploy(DeploymentPlan plan, string? operationId = null) =>
            new DeploymentEngine(Staging).Execute(plan, operationId);

        public void WriteStorageFile(string folder, string relative, string content) =>
            WriteFile(Path.Combine(Storage, folder), relative, content);

        public void WriteGameEnabledFile(string folder, string relative, string content) =>
            WriteFile(Path.Combine(GameMods, folder), relative, content);

        public void WriteGameDisabledFile(string folder, string relative, string content) =>
            WriteFile(Path.Combine(GameMods, "~" + folder), relative, content);

        private static void WriteFile(string root, string relative, string content)
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch { /* temp cleanup */ }
        }
    }
}
