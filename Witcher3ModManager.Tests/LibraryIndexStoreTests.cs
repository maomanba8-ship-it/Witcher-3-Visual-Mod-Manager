using System;
using System.IO;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Witcher3ModManager.Core.Storage;
using Xunit;

namespace Witcher3ModManager.Tests;

public class LibraryIndexStoreTests
{
    [Fact]
    public void A_missing_index_loads_as_empty()
    {
        var dir = NewDir();
        try
        {
            var store = new LibraryIndexStore(Path.Combine(dir, "library.json"));

            var index = store.Load();

            Assert.Empty(index.Packages);
            Assert.Equal(TextFileOutcome.Missing, store.LastLoadOutcome);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_saved_index_can_be_loaded_back()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "library.json");
            var store = new LibraryIndexStore(path);
            var recorded = LibraryIndexUpdater.WithPackage(null, "pkg1", "Mod One", new[]
            {
                new ComponentRecord("c1", TargetDomain.Mods, "modOne", new[]
                {
                    new TargetRecord(@"mods\modOne", TargetKind.Folder, "hash1", DateTime.UnixEpoch)
                })
            });

            Assert.True(store.Save(recorded));

            var reloaded = new LibraryIndexStore(path).Load();
            Assert.Equal("c1", reloaded.OwnerOf("mods/modone")!.ComponentId);
            Assert.Equal("hash1", reloaded.OwnerOf("mods/modone")!.Targets[0].WrittenHash);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Saving_twice_keeps_a_backup_of_the_previous_index()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "library.json");
            var store = new LibraryIndexStore(path);

            store.Save(LibraryIndexUpdater.WithPackage(null, "pkgOld", "Old", Array.Empty<ComponentRecord>()));
            store.Save(LibraryIndexUpdater.WithPackage(null, "pkgNew", "New", Array.Empty<ComponentRecord>()));

            Assert.True(File.Exists(path + AtomicFile.BackupSuffix));
            Assert.Contains("pkgOld", File.ReadAllText(path + AtomicFile.BackupSuffix));
            Assert.Contains("pkgNew", File.ReadAllText(path));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_damaged_index_falls_back_to_the_backup_and_is_copied_aside()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "library.json");
            var store = new LibraryIndexStore(path);
            store.Save(LibraryIndexUpdater.WithPackage(null, "pkgGood", "Good", Array.Empty<ComponentRecord>()));
            store.Save(LibraryIndexUpdater.WithPackage(null, "pkgNewer", "Newer", Array.Empty<ComponentRecord>()));

            File.WriteAllText(path, "{ this is not json");

            var damaged = new LibraryIndexStore(path);
            var reloaded = damaged.Load();

            // The previous good index is used, and the damaged file is preserved.
            Assert.Equal("pkgGood", Assert.Single(reloaded.Packages).PackageId);
            Assert.Equal(TextFileOutcome.RestoredFromBackup, damaged.LastLoadOutcome);
            Assert.NotNull(damaged.DamagedCopyPath);
            Assert.NotEmpty(Directory.GetFiles(dir, "*" + TextFileRecovery.DamagedSuffix + "*"));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void An_index_from_a_newer_version_is_ignored_rather_than_misread()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "library.json");
            File.WriteAllText(path, "{ \"Version\": 99, \"Packages\": [] }");

            var index = new LibraryIndexStore(path).Load();

            Assert.Empty(index.Packages);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_blank_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new LibraryIndexStore(""));
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w3mm_index_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
