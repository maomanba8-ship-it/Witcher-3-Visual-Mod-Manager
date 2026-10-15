using System.Collections.Generic;
using Witcher3ModManager.Core.Import;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ArchiveExtractionPolicyTests
{
    [Fact]
    public void A_normal_mod_archive_is_allowed()
    {
        var entries = new[]
        {
            new ArchiveEntryInfo("mods/modX/content/readme.txt", 24),
            new ArchiveEntryInfo("mods/modX/", 0, IsDirectory: true),
            new ArchiveEntryInfo("dlc/dlcY/content/bundle.bundle", 4096)
        };

        var verdict = ArchiveExtractionPolicy.Evaluate(entries);

        Assert.True(verdict.IsSafe);
        Assert.Equal(ExtractionRejection.None, verdict.Rejection);
    }

    [Fact]
    public void An_empty_or_missing_listing_is_allowed()
    {
        Assert.True(ArchiveExtractionPolicy.Evaluate(null).IsSafe);
        Assert.True(ArchiveExtractionPolicy.Evaluate(new List<ArchiveEntryInfo>()).IsSafe);
    }

    [Theory]
    [InlineData("../outside.txt", ExtractionRejection.ParentTraversal)]
    [InlineData("mods/../../outside.txt", ExtractionRejection.ParentTraversal)]
    [InlineData(@"mods\..\..\outside.txt", ExtractionRejection.ParentTraversal)]
    [InlineData("/etc/passwd", ExtractionRejection.AbsolutePath)]
    [InlineData(@"\windows\system32\evil.dll", ExtractionRejection.AbsolutePath)]
    [InlineData(@"C:\windows\system32\evil.dll", ExtractionRejection.DriveQualified)]
    [InlineData("c:/windows/evil.dll", ExtractionRejection.DriveQualified)]
    [InlineData("readme.txt:payload", ExtractionRejection.InvalidEntryName)]
    [InlineData("   ", ExtractionRejection.EmptyEntryName)]
    public void Entries_that_would_land_outside_the_destination_are_refused(
        string path, ExtractionRejection expected)
    {
        var verdict = ArchiveExtractionPolicy.Evaluate(new[] { new ArchiveEntryInfo(path, 1) });

        Assert.False(verdict.IsSafe);
        Assert.Equal(expected, verdict.Rejection);
        Assert.Equal(path, verdict.Detail);
    }

    [Fact]
    public void Too_many_files_are_refused()
    {
        var limits = new ArchiveExtractionLimits(MaxEntries: 3);
        var entries = new List<ArchiveEntryInfo>();
        for (var i = 0; i < 4; i++) entries.Add(new ArchiveEntryInfo($"file{i}.txt", 1));

        var verdict = ArchiveExtractionPolicy.Evaluate(entries, limits);

        Assert.False(verdict.IsSafe);
        Assert.Equal(ExtractionRejection.TooManyEntries, verdict.Rejection);
    }

    [Fact]
    public void Directories_do_not_count_towards_the_file_limit()
    {
        var limits = new ArchiveExtractionLimits(MaxEntries: 2);
        var entries = new[]
        {
            new ArchiveEntryInfo("a/", 0, IsDirectory: true),
            new ArchiveEntryInfo("b/", 0, IsDirectory: true),
            new ArchiveEntryInfo("a/x.txt", 1),
            new ArchiveEntryInfo("b/y.txt", 1)
        };

        Assert.True(ArchiveExtractionPolicy.Evaluate(entries, limits).IsSafe);
    }

    [Fact]
    public void An_oversized_archive_is_refused()
    {
        var limits = new ArchiveExtractionLimits(MaxTotalBytes: 1000);
        var entries = new[]
        {
            new ArchiveEntryInfo("big.bundle", 900),
            new ArchiveEntryInfo("another.bundle", 200)
        };

        var verdict = ArchiveExtractionPolicy.Evaluate(entries, limits);

        Assert.False(verdict.IsSafe);
        Assert.Equal(ExtractionRejection.ArchiveTooLarge, verdict.Rejection);
    }

    [Fact]
    public void Claimed_sizes_that_would_overflow_the_sum_are_still_refused()
    {
        var entries = new[]
        {
            new ArchiveEntryInfo("a.bundle", long.MaxValue),
            new ArchiveEntryInfo("b.bundle", long.MaxValue)
        };

        var verdict = ArchiveExtractionPolicy.Evaluate(entries);

        Assert.False(verdict.IsSafe);
        Assert.Equal(ExtractionRejection.ArchiveTooLarge, verdict.Rejection);
    }

    [Fact]
    public void An_over_long_path_is_refused()
    {
        var limits = new ArchiveExtractionLimits(MaxPathLength: 20);
        var verdict = ArchiveExtractionPolicy.Evaluate(
            new[] { new ArchiveEntryInfo(new string('a', 21) + ".txt", 1) }, limits);

        Assert.False(verdict.IsSafe);
        Assert.Equal(ExtractionRejection.PathTooLong, verdict.Rejection);
    }
}
