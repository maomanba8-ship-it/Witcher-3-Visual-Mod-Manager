using System;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Storage;
using Xunit;

namespace Witcher3ModManager.Tests;

public class AtomicFileTests
{
    [Fact]
    public void Writing_a_new_file_creates_it_without_a_backup()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.settings");
        try
        {
            AtomicFile.WriteAllLines(path, new[] { "\"modA\" = 1" });

            Assert.Equal(new[] { "\"modA\" = 1" }, File.ReadAllLines(path));
            Assert.False(File.Exists(path + AtomicFile.BackupSuffix));   // nothing existed to keep
            AssertNoTempLeftBehind(dir);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Overwriting_keeps_the_previous_content_as_a_backup()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.settings");
        try
        {
            AtomicFile.WriteAllText(path, "\"modA\" = 1");
            AtomicFile.WriteAllText(path, "\"modA\" = 2");

            Assert.Equal("\"modA\" = 2", File.ReadAllText(path));
            Assert.Equal("\"modA\" = 1", File.ReadAllText(path + AtomicFile.BackupSuffix));
            AssertNoTempLeftBehind(dir);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Repeated_writes_keep_the_backup_at_one_generation_not_a_chain()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "mods.settings");
        try
        {
            AtomicFile.WriteAllText(path, "v1");
            AtomicFile.WriteAllText(path, "v2");
            AtomicFile.WriteAllText(path, "v3");

            Assert.Equal("v3", File.ReadAllText(path));
            Assert.Equal("v2", File.ReadAllText(path + AtomicFile.BackupSuffix));

            // Exactly the file and its single-generation backup; no .tmp, no .bak.bak.
            var files = Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.Equal(2, files.Count);
            Assert.Contains(path, files);
            Assert.Contains(path + AtomicFile.BackupSuffix, files);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void It_creates_a_missing_directory_on_the_way()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "nested", "deeper", "mods.settings");
        try
        {
            AtomicFile.WriteAllText(path, "content");

            Assert.Equal("content", File.ReadAllText(path));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_failed_write_throws_and_leaves_no_temporary_file()
    {
        var dir = NewDir();
        var occupied = Path.Combine(dir, "already-a-directory");
        Directory.CreateDirectory(occupied);
        try
        {
            // The target is a directory, so the swap cannot succeed.
            Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(occupied, "content"));
            AssertNoTempLeftBehind(dir);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Blank_paths_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => AtomicFile.WriteAllText("", "content"));
        Assert.Throws<ArgumentException>(() => AtomicFile.WriteAllLines("   ", new[] { "x" }));
    }

    private static void AssertNoTempLeftBehind(string dir) =>
        Assert.Empty(Directory.GetFiles(dir, "*" + AtomicFile.TempSuffix, SearchOption.AllDirectories));

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w3mm_atomic_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
