using System;
using System.IO;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class StagingReportTests : IDisposable
{
    private readonly string _game;

    public StagingReportTests()
    {
        _game = Path.Combine(Path.GetTempPath(), "w3mm_staging_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_game, StagingReport.FolderName));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_game)) Directory.Delete(_game, recursive: true); }
        catch { /* temp cleanup */ }
    }

    [Fact]
    public void A_handled_operation_with_staging_left_is_reported_with_its_size()
    {
        Directory.CreateDirectory(Path.Combine(_game, StagingReport.FolderName, "op1", "occupied"));
        File.WriteAllText(Path.Combine(_game, StagingReport.FolderName, "op1", "occupied", "a.txt"), "12345");

        var leftover = Assert.Single(StagingReport.Find(_game, new[] { "op1" }));

        Assert.Equal("op1", leftover.OperationId);
        Assert.Equal(5, leftover.Bytes);
        Assert.True(Directory.Exists(leftover.Path));
    }

    [Fact]
    public void Operations_that_left_nothing_are_not_reported()
    {
        Assert.Empty(StagingReport.Find(_game, new[] { "never-started", "", "   " }));
        Assert.Empty(StagingReport.Find(_game, null));
    }

    [Fact]
    public void Another_operations_staging_is_left_alone()
    {
        Directory.CreateDirectory(Path.Combine(_game, StagingReport.FolderName, "op2"));

        Assert.Empty(StagingReport.Find(_game, new[] { "op1" }));
        Assert.True(Directory.Exists(Path.Combine(_game, StagingReport.FolderName, "op2")));
    }

    [Fact]
    public void Without_a_game_or_staging_folder_there_is_nothing_to_report()
    {
        Assert.Empty(StagingReport.Find(null, new[] { "op1" }));
        Assert.Empty(StagingReport.Find(Path.Combine(_game, "missing"), new[] { "op1" }));
    }

    [Fact]
    public void Nothing_outside_the_staging_folder_is_ever_looked_at()
    {
        // A journal name that tries to climb out of the staging folder must not resolve to anything.
        var escaped = Path.Combine(_game, "mods");
        Directory.CreateDirectory(escaped);

        Assert.Empty(StagingReport.Find(_game, new[] { ".." + Path.DirectorySeparatorChar + "mods" }));
    }
}
