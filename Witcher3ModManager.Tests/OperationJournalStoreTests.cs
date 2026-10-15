using System;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class OperationJournalStoreTests
{
    [Fact]
    public void A_finished_operation_leaves_nothing_behind()
    {
        var dir = NewDir();
        try
        {
            var store = new OperationJournalStore(dir);

            var journal = store.Begin(OperationKind.Enable, "modA", new[] { @"mods\modA" });
            Assert.NotNull(journal);
            Assert.Single(store.Leftovers());       // present while it runs

            store.Complete(journal!.OperationId);

            Assert.Empty(store.Leftovers());
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_failed_operation_is_kept_with_its_reason_and_planned_targets()
    {
        var dir = NewDir();
        try
        {
            var store = new OperationJournalStore(dir);
            var journal = store.Begin(OperationKind.Delete, "modA", new[] { @"mods\modA", @"dlc\dlcA" });

            store.Fail(journal, "could not delete mods/modA");

            var leftover = Assert.Single(store.Leftovers());
            Assert.Equal(OperationKind.Delete, leftover.Kind);
            Assert.Equal("modA", leftover.Subject);
            Assert.Equal(new[] { @"mods\modA", @"dlc\dlcA" }, leftover.Targets);
            Assert.False(leftover.Completed);
            Assert.Equal("could not delete mods/modA", leftover.Failure);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Leftovers_are_readable_again_by_a_later_run()
    {
        var dir = NewDir();
        try
        {
            var journal = new OperationJournalStore(dir).Begin(OperationKind.Import, "modB", new[] { "mods/modB" });
            new OperationJournalStore(dir).Fail(journal, "interrupted");

            // A fresh store, as a new process would have.
            var leftovers = new OperationJournalStore(dir).Leftovers();

            Assert.Equal("modB", Assert.Single(leftovers).Subject);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void An_unreadable_journal_is_still_reported()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "abc123.json"), "{ not json");

            var leftover = Assert.Single(new OperationJournalStore(dir).Leftovers());

            Assert.Equal("abc123", leftover.OperationId);
            Assert.False(leftover.Completed);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void An_empty_or_missing_journal_directory_reports_nothing()
    {
        var dir = NewDir();
        try
        {
            Assert.Empty(new OperationJournalStore(dir).Leftovers());
            Assert.Empty(new OperationJournalStore(Path.Combine(dir, "never-created")).Leftovers());
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Leftovers_are_ordered_oldest_first()
    {
        var dir = NewDir();
        try
        {
            var store = new OperationJournalStore(dir);
            var first = store.Begin(OperationKind.Enable, "first", Array.Empty<string>());
            var second = store.Begin(OperationKind.Enable, "second", Array.Empty<string>());
            store.Fail(first, "x");
            store.Fail(second, "y");

            var subjects = store.Leftovers().Select(l => l.Subject).ToArray();

            Assert.Equal(new[] { "first", "second" }, subjects);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Completing_an_unknown_or_blank_operation_is_harmless()
    {
        var dir = NewDir();
        try
        {
            var store = new OperationJournalStore(dir);
            store.Complete(null);
            store.Complete("");
            store.Complete("never-started");
            store.Fail(null, "ignored");

            Assert.Empty(store.Leftovers());
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w3mm_journal_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
