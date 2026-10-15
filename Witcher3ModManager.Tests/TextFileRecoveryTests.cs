using System;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Storage;
using Xunit;

namespace Witcher3ModManager.Tests;

public class TextFileRecoveryTests
{
    [Fact]
    public void A_missing_file_is_not_a_problem()
    {
        var dir = NewDir();
        try
        {
            var load = TextFileRecovery.Load(Path.Combine(dir, "mods.json"), _ => true);

            Assert.Equal(TextFileOutcome.Missing, load.Outcome);
            Assert.False(load.IsUsable);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_usable_file_is_read_as_is()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.json");
        try
        {
            File.WriteAllText(path, "good");

            var load = TextFileRecovery.Load(path, t => t == "good");

            Assert.Equal(TextFileOutcome.Loaded, load.Outcome);
            Assert.Equal("good", load.Text);
            Assert.Empty(DamagedCopies(dir));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_damaged_file_falls_back_to_the_backup_and_keeps_the_damaged_one()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.json");
        try
        {
            File.WriteAllText(path, "damaged");
            File.WriteAllText(path + AtomicFile.BackupSuffix, "good");

            var load = TextFileRecovery.Load(path, IsGood);

            Assert.Equal(TextFileOutcome.RestoredFromBackup, load.Outcome);
            Assert.Equal("good", load.Text);

            // The damaged file is copied aside before anything can overwrite it.
            var preserved = Assert.Single(DamagedCopies(dir));
            Assert.Equal("damaged", File.ReadAllText(preserved));
            Assert.Equal(preserved, load.Detail);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_damaged_file_without_a_backup_is_reported_as_damaged()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.json");
        try
        {
            File.WriteAllText(path, "damaged");

            var load = TextFileRecovery.Load(path, IsGood);

            Assert.Equal(TextFileOutcome.Damaged, load.Outcome);
            Assert.False(load.IsUsable);
            Assert.Single(DamagedCopies(dir));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_damaged_backup_does_not_make_things_worse()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.json");
        try
        {
            File.WriteAllText(path, "damaged");
            File.WriteAllText(path + AtomicFile.BackupSuffix, "also-damaged");

            var load = TextFileRecovery.Load(path, IsGood);

            Assert.Equal(TextFileOutcome.Damaged, load.Outcome);
            Assert.Single(DamagedCopies(dir));   // only the main file is copied aside
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_left_completely_alone()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.json");
        try
        {
            File.WriteAllText(path, "good");
            File.WriteAllText(path + AtomicFile.BackupSuffix, "older");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var load = TextFileRecovery.Load(path, IsGood);

                // A lock says nothing about the content, so neither the backup nor a damaged
                // copy is produced.
                Assert.Equal(TextFileOutcome.ReadFailed, load.Outcome);
                Assert.False(load.IsUsable);
                Assert.Empty(DamagedCopies(dir));
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Blank_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => TextFileRecovery.Load("", _ => true));
        Assert.Throws<ArgumentNullException>(() => TextFileRecovery.Load("x", null!));
    }

    private static bool IsGood(string text) => text == "good";

    private static string[] DamagedCopies(string dir) =>
        Directory.GetFiles(dir, "*" + TextFileRecovery.DamagedSuffix + "*");

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w3mm_recovery_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
