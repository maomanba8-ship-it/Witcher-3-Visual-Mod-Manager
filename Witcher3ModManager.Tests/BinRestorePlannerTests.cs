using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class BinRestorePlannerTests
{
    private const string InputXml = "config/r4game/user_config_matrix/pc/input.xml";
    private static readonly DateTime When = new(2026, 10, 16, 12, 0, 0);

    [Fact]
    public void Content_that_was_there_before_is_copied_back_from_the_store()
    {
        using var world = new World();
        var originalHash = world.Keep("ORIGINAL");

        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "writtenHash", originalHash, When);

        var plan = world.Plan(ledger, "modA", current: "writtenHash");

        var step = Assert.Single(plan.Steps.Steps);
        Assert.Equal(TargetDomain.Bin, step.Domain);
        Assert.Equal(DeploymentAction.Copy, step.Action);
        Assert.Equal(world.Backups.PathFor(originalHash), step.SourcePath);
        Assert.Equal(world.GamePath(InputXml), step.DestinationPath);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void A_file_we_created_is_taken_away_rather_than_put_back()
    {
        using var world = new World();
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "writtenHash", shaBeforeWrite: null, When);

        var plan = world.Plan(ledger, "modA", current: "writtenHash");

        var step = Assert.Single(plan.Steps.Steps);
        // A stash, not a delete: the engine moves it aside and can undo that if a later step fails.
        Assert.Equal(DeploymentAction.Stash, step.Action);
        Assert.Equal(world.GamePath(InputXml), step.SourcePath);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void What_a_later_package_wrote_is_what_comes_back()
    {
        using var world = new World();
        var firstHash = world.Keep("FIRST");
        var secondHash = world.Keep("SECOND");

        var ledger = BinLedger.Empty
            .WithWrite(InputXml, "modA", firstHash, "original", When)
            .WithWrite(InputXml, "modB", secondHash, firstHash, When.AddMinutes(1));

        var plan = world.Plan(ledger, "modB", current: secondHash);

        Assert.Equal(world.Backups.PathFor(firstHash), Assert.Single(plan.Steps.Steps).SourcePath);
    }

    [Fact]
    public void Content_someone_else_changed_is_left_alone_and_named()
    {
        using var world = new World();
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "writtenHash", "original", When);

        var plan = world.Plan(ledger, "modA", current: "someoneElse");

        Assert.Empty(plan.Steps.Steps);
        var note = Assert.Single(plan.Notes);
        Assert.Equal(OperationNoteKind.BinModifiedElsewhere, note.Kind);
        Assert.Equal(TargetDomain.Bin, note.Domain);
        Assert.Equal(InputXml, note.Subject);
    }

    [Fact]
    public void A_package_on_record_as_having_written_the_file_is_told_it_cannot_be_put_back()
    {
        using var world = new World();
        // The ledger has nothing (the write happened before the ledger existed), but the ownership
        // index does.
        var plan = world.Plan(BinLedger.Empty, "modA", current: "writtenHash", onRecord: new[] { InputXml });

        Assert.Empty(plan.Steps.Steps);
        var note = Assert.Single(plan.Notes);
        Assert.Equal(OperationNoteKind.BinNoRecord, note.Kind);
        Assert.Equal(InputXml, note.Subject);
    }

    [Fact]
    public void A_file_the_package_never_wrote_is_not_news()
    {
        using var world = new World();

        // A fresh import that was never enabled: the file on disk belongs to something else.
        var plan = world.Plan(BinLedger.Empty, "modB", current: "someoneElsesHash");

        Assert.Empty(plan.Steps.Steps);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void A_file_that_is_already_gone_is_a_clean_state_not_a_warning()
    {
        using var world = new World();
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "writtenHash", "original", When);

        var plan = world.Plan(ledger, "modA", current: null);

        Assert.Empty(plan.Steps.Steps);
        // Nothing is in the game and nothing is claimed: there is nothing for the user to do.
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void Losing_the_kept_content_is_reported_rather_than_skipped_silently()
    {
        using var world = new World();
        // The ledger points at content that is not in the store any more.
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "writtenHash", "missingBlob", When);

        var plan = world.Plan(ledger, "modA", current: "writtenHash");

        Assert.Empty(plan.Steps.Steps);
        Assert.Equal(OperationNoteKind.BinBackupMissing, Assert.Single(plan.Notes).Kind);
    }

    [Fact]
    public void Without_a_game_directory_or_a_store_there_is_nothing_to_plan()
    {
        using var world = new World();
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "writtenHash", "original", When);

        Assert.Empty(BinRestorePlanner.Plan(ledger, world.Backups, "modA", null, new[] { InputXml }, _ => "writtenHash").Steps.Steps);
        Assert.Empty(BinRestorePlanner.Plan(ledger, null, "modA", world.GameBin, new[] { InputXml }, _ => "writtenHash").Steps.Steps);
        Assert.Empty(BinRestorePlanner.Plan(ledger, world.Backups, "modA", world.GameBin, Array.Empty<string>(), _ => "writtenHash").Steps.Steps);
    }

    [Fact]
    public void Several_files_are_planned_in_one_pass_including_when_they_differ()
    {
        using var world = new World();
        var kept = world.Keep("ORIGINAL");
        const string NewFile = "config/added.xml";

        var ledger = BinLedger.Empty
            .WithWrite(InputXml, "modA", "writtenHash", kept, When)
            .WithWrite(NewFile, "modA", "writtenNew", shaBeforeWrite: null, When.AddMinutes(1));

        // What is on disk differs per file, so the current content has to be answered per path.
        var currents = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [InputXml] = "writtenHash",
            [NewFile] = "writtenNew"
        };

        var plan = BinRestorePlanner.Plan(ledger, world.Backups, "modA", world.GameBin,
            new[] { InputXml, NewFile }, path => currents.TryGetValue(path, out var hash) ? hash : null).Steps.Steps;

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, s => s.Action == DeploymentAction.Copy && s.Subject == InputXml);
        Assert.Contains(plan, s => s.Action == DeploymentAction.Stash && s.Subject == NewFile);
    }

    /// <summary>A game directory and a backup store in a throwaway folder.</summary>
    private sealed class World : IDisposable
    {
        private readonly string _root;

        public World()
        {
            _root = Path.Combine(Path.GetTempPath(), "w3mm_binrestore_" + Guid.NewGuid().ToString("N")[..8]);
            GameBin = Path.Combine(_root, "game", "bin");
            Directory.CreateDirectory(GameBin);
            Backups = new BinBackupStore(Path.Combine(_root, "binbackup"));
        }

        public string GameBin { get; }
        public BinBackupStore Backups { get; }

        /// <summary>Puts content in the store, as the enable step would have, and returns its hash.</summary>
        public string Keep(string content)
        {
            var source = Path.Combine(_root, "content-" + Guid.NewGuid().ToString("N")[..6] + ".xml");
            File.WriteAllText(source, content);
            return Backups.Capture(source)!;
        }

        public string GamePath(string relative) =>
            Path.Combine(GameBin, relative.Replace('/', Path.DirectorySeparatorChar));

        public BinRestorePlan Plan(BinLedger ledger, string packageId, string? current,
            IEnumerable<string>? onRecord = null) =>
            BinRestorePlanner.Plan(ledger, Backups, packageId, GameBin, new[] { InputXml }, _ => current, onRecord);

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch { /* temp cleanup */ }
        }
    }
}
