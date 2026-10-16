using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class BinLedgerTests : IDisposable
{
    private const string InputXml = "config/r4game/user_config_matrix/pc/input.xml";
    private static readonly DateTime When = new(2026, 10, 16, 12, 0, 0);
    private readonly List<string> _tempRoots = new();

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* a leftover temp folder is not worth failing a test over */ }
        }
    }

    [Fact]
    public void The_first_write_remembers_what_was_there_before()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "hashA", shaBeforeWrite: "original", When);

        var record = Assert.IsType<BinPathRecord>(ledger.Find(InputXml));
        Assert.Equal("original", record.Original);
        Assert.Equal("hashA", Assert.Single(record.Writers).Sha256);
    }

    [Fact]
    public void A_second_write_keeps_the_first_original_and_records_who_wrote_last()
    {
        var ledger = BinLedger.Empty
            .WithWrite(InputXml, "modA", "hashA", "original", When)
            .WithWrite(InputXml, "modB", "hashB", "hashA", When.AddMinutes(1));

        var record = Assert.IsType<BinPathRecord>(ledger.Find(InputXml));
        // The state to go back to is still the real original, not what modA had written.
        Assert.Equal("original", record.Original);
        Assert.Equal(new[] { "modA", "modB" }, record.Writers.Select(w => w.PackageId));
    }

    [Fact]
    public void Writing_again_replaces_that_packages_claim_instead_of_stacking_twice()
    {
        var ledger = BinLedger.Empty
            .WithWrite(InputXml, "modA", "hashA", "original", When)
            .WithWrite(InputXml, "modB", "hashB", "hashA", When.AddMinutes(1))
            .WithWrite(InputXml, "modA", "hashA2", "hashB", When.AddMinutes(2));

        var record = Assert.IsType<BinPathRecord>(ledger.Find(InputXml));
        Assert.Equal(new[] { "modB", "modA" }, record.Writers.Select(w => w.PackageId));
        Assert.Equal("hashA2", record.Writers[^1].Sha256);
    }

    [Fact]
    public void Separators_and_case_do_not_matter_when_looking_a_path_up()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "hashA", "original", When);

        Assert.NotNull(ledger.Find(@"config\r4game\user_config_matrix\pc\input.xml"));
        Assert.NotNull(ledger.Find("CONFIG/r4game/User_Config_Matrix/pc/input.XML"));
        Assert.Null(ledger.Find("config/other.xml"));
    }

    [Fact]
    public void Restoring_after_another_package_put_its_content_back_uses_that_packages_content()
    {
        var ledger = BinLedger.Empty
            .WithWrite(InputXml, "modA", "hashA", "original", When)
            .WithWrite(InputXml, "modB", "hashB", "hashA", When.AddMinutes(1));

        // modB is the one being disabled, and its content is what is on disk.
        var step = Assert.Single(BinLedger.PlanRestore(ledger, "modB", new[] { InputXml }, _ => "hashB"));

        Assert.Equal(BinRestoreAction.WriteBack, step.Action);
        Assert.Equal("hashA", step.Sha256);
        Assert.Equal(BinRestoreReason.Superseded, step.Reason);
    }

    [Fact]
    public void Restoring_the_only_writer_goes_back_to_what_was_there_at_the_start()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "hashA", "original", When);

        var step = Assert.Single(BinLedger.PlanRestore(ledger, "modA", new[] { InputXml }, _ => "hashA"));

        Assert.Equal(BinRestoreAction.WriteBack, step.Action);
        Assert.Equal("original", step.Sha256);
        Assert.Equal(BinRestoreReason.BackToOriginal, step.Reason);
    }

    [Fact]
    public void A_file_we_created_is_removed_because_nothing_was_there_before()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "hashA", shaBeforeWrite: null, When);

        var step = Assert.Single(BinLedger.PlanRestore(ledger, "modA", new[] { InputXml }, _ => "hashA"));

        Assert.Equal(BinRestoreAction.Remove, step.Action);
        Assert.Null(step.Sha256);
        Assert.Equal(BinRestoreReason.CreatedByUs, step.Reason);
    }

    [Fact]
    public void Content_that_is_not_what_we_wrote_is_left_alone()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "hashA", "original", When);

        var step = Assert.Single(BinLedger.PlanRestore(ledger, "modA", new[] { InputXml }, _ => "someoneElse"));

        Assert.Equal(BinRestoreAction.LeaveAlone, step.Action);
        Assert.Equal(BinRestoreReason.ModifiedElsewhere, step.Reason);
    }

    [Fact]
    public void A_file_that_is_already_gone_is_left_gone()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "hashA", "original", When);

        var step = Assert.Single(BinLedger.PlanRestore(ledger, "modA", new[] { InputXml }, _ => null));

        Assert.Equal(BinRestoreAction.LeaveAlone, step.Action);
        Assert.Equal(BinRestoreReason.Missing, step.Reason);
    }

    [Fact]
    public void Without_a_record_nothing_is_touched()
    {
        var step = Assert.Single(BinLedger.PlanRestore(BinLedger.Empty, "modA", new[] { InputXml }, _ => "hashA"));

        Assert.Equal(BinRestoreAction.LeaveAlone, step.Action);
        // Nothing says this package ever wrote the file, so there is nothing to report either.
        Assert.Equal(BinRestoreReason.NotOurs, step.Reason);
    }

    [Fact]
    public void A_path_written_only_by_someone_else_is_not_ours_to_restore()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modB", "hashB", "original", When);

        var step = Assert.Single(BinLedger.PlanRestore(ledger, "modA", new[] { InputXml }, _ => "hashB"));

        Assert.Equal(BinRestoreReason.NotOurs, step.Reason);
    }

    [Fact]
    public void A_package_on_record_as_having_written_the_path_is_told_it_has_no_record()
    {
        // The ownership index remembers writes from before the ledger existed, so this is the case
        // where "no record" is genuine news: the file is there and cannot be put back.
        var step = Assert.Single(BinLedger.PlanRestore(BinLedger.Empty, "modA", new[] { InputXml },
            _ => "hashA", pathsOnRecord: new[] { InputXml }));

        Assert.Equal(BinRestoreAction.LeaveAlone, step.Action);
        Assert.Equal(BinRestoreReason.NoRecord, step.Reason);
    }

    [Fact]
    public void Dropping_the_last_claim_forgets_the_path()
    {
        var ledger = BinLedger.Empty
            .WithWrite(InputXml, "modA", "hashA", "original", When)
            .WithWrite(InputXml, "modB", "hashB", "hashA", When.AddMinutes(1));

        var afterB = ledger.WithoutWriter(InputXml, "modB");
        Assert.Equal("modA", Assert.Single(afterB.Find(InputXml)!.Writers).PackageId);

        var afterA = afterB.WithoutWriter(InputXml, "modA");
        Assert.Null(afterA.Find(InputXml));
        Assert.Equal(0, afterA.Count);
    }

    [Fact]
    public void Plans_cover_every_path_once_and_ignore_blanks()
    {
        var ledger = BinLedger.Empty.WithWrite(InputXml, "modA", "hashA", "original", When);

        var steps = BinLedger.PlanRestore(ledger, "modA",
            new[] { InputXml, InputXml.Replace('/', '\\'), "", "   " }, _ => "hashA");

        Assert.Single(steps);
    }

    [Fact]
    public void The_ledger_survives_a_save_and_load()
    {
        var store = new BinLedgerStore(Path.Combine(TempRoot(), "bin-ledger.json"));
        var ledger = BinLedger.Empty
            .WithWrite(InputXml, "modA", "hashA", "original", When)
            .WithWrite(InputXml, "modB", "hashB", "hashA", When.AddMinutes(1));

        Assert.True(store.Save(ledger));
        var loaded = store.Load();

        Assert.Equal("original", loaded.Find(InputXml)!.Original);
        Assert.Equal(new[] { "modA", "modB" }, loaded.Find(InputXml)!.Writers.Select(w => w.PackageId));
        Assert.Equal(BinRestoreReason.Superseded,
            Assert.Single(BinLedger.PlanRestore(loaded, "modB", new[] { InputXml }, _ => "hashB")).Reason);
    }

    [Fact]
    public void A_damaged_ledger_is_preserved_rather_than_replaced_by_an_empty_one()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "bin-ledger.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new BinLedgerStore(path);

        var loaded = store.Load();

        Assert.Equal(0, loaded.Count);
        Assert.NotEqual(Witcher3ModManager.Core.Storage.TextFileOutcome.Loaded, store.LastLoadOutcome);
        // The damaged file is still where it was, so nothing the user had is lost.
        Assert.True(File.Exists(path));
        Assert.NotNull(store.DamagedCopyPath);
    }

    [Fact]
    public void The_backup_store_keeps_one_copy_per_content()
    {
        var root = TempRoot();
        var store = new BinBackupStore(Path.Combine(root, "binbackup"));
        var first = Path.Combine(root, "first.xml");
        var second = Path.Combine(root, "second.xml");
        File.WriteAllText(first, "SAME");
        File.WriteAllText(second, "SAME");

        var hashOfFirst = store.Capture(first);
        var hashOfSecond = store.Capture(second);

        Assert.NotNull(hashOfFirst);
        Assert.Equal(hashOfFirst, hashOfSecond);
        Assert.Single(Directory.GetFiles(store.Root));
        Assert.Equal(hashOfFirst, BinBackupStore.HashOf(first));
    }

    [Fact]
    public void A_captured_file_can_be_put_back()
    {
        var root = TempRoot();
        var store = new BinBackupStore(Path.Combine(root, "binbackup"));
        var original = Path.Combine(root, "original.xml");
        File.WriteAllText(original, "ORIGINAL CONTENT");
        var hash = store.Capture(original);

        // The game file is then overwritten by a mod...
        var gamePath = Path.Combine(root, "game", "bin", "config", "input.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(gamePath)!);
        File.WriteAllText(gamePath, "MOD CONTENT");

        // ...and the restore puts the captured content back, creating the folder if needed.
        Assert.True(store.TryRestore(hash, gamePath));
        Assert.Equal("ORIGINAL CONTENT", File.ReadAllText(gamePath));
    }

    [Fact]
    public void Capturing_something_that_is_not_there_yields_no_hash()
    {
        var store = new BinBackupStore(Path.Combine(TempRoot(), "binbackup"));

        Assert.Null(store.Capture(Path.Combine(TempRoot(), "missing.xml")));
        Assert.Null(BinBackupStore.HashOf(null));
        Assert.False(store.TryRestore(null, Path.Combine(TempRoot(), "whatever.xml")));
        Assert.False(store.TryRestore("0123456789abcdef", Path.Combine(TempRoot(), "whatever.xml")));
    }

    private string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "w3mm_binledger_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        _tempRoots.Add(path);
        return path;
    }
}
