using System.Linq;
using Witcher3ModManager.Core.Import;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ArchiveListingParserTests
{
    // Shape of "7z l -slt": a header block for the archive itself, then one block per entry.
    private const string Sample = """
        7-Zip 23.01 (x64) : Copyright (c) 1999-2023 Igor Pavlov : 2023-06-20

        Scanning the drive for archives:
        1 file, 12345 bytes (13 KiB)

        Listing archive: sample.zip

        --
        Path = sample.zip
        Type = zip
        Physical Size = 12345

        ----------
        Path = mods/modX/content/readme.txt
        Folder = -
        Size = 24
        Packed Size = 24
        Attributes = A

        Path = mods/modX/bin
        Folder = +
        Size = 0

        Path = second.txt
        Size = 5
        """;

    [Fact]
    public void It_reads_every_entry_and_skips_the_archive_header()
    {
        var entries = ArchiveListingParser.Parse(Sample);

        Assert.Equal(3, entries.Count);

        Assert.Equal("mods/modX/content/readme.txt", entries[0].RelativePath);
        Assert.Equal(24, entries[0].Size);
        Assert.False(entries[0].IsDirectory);

        Assert.Equal("mods/modX/bin", entries[1].RelativePath);
        Assert.True(entries[1].IsDirectory);

        Assert.Equal("second.txt", entries[2].RelativePath);
        Assert.Equal(5, entries[2].Size);
    }

    [Fact]
    public void It_tolerates_missing_sizes_and_noise_lines()
    {
        var listing = "Path = a.txt\nComment = no size here\n\nPath = b.txt\nSize = not-a-number\n";

        var entries = ArchiveListingParser.Parse(listing);

        Assert.Equal(new[] { "a.txt", "b.txt" }, entries.Select(e => e.RelativePath).ToArray());
        Assert.All(entries, e => Assert.Equal(0, e.Size));
    }

    [Fact]
    public void Blank_input_yields_nothing()
    {
        Assert.Empty(ArchiveListingParser.Parse(null));
        Assert.Empty(ArchiveListingParser.Parse(""));
        Assert.Empty(ArchiveListingParser.Parse("   "));
        Assert.Empty(ArchiveListingParser.Parse("no entries at all"));
    }

    [Fact]
    public void A_parsed_listing_can_be_fed_straight_into_the_safety_policy()
    {
        var listing = "Path = mods/modX/content/readme.txt\nSize = 24\n\nPath = ../../escape.txt\nSize = 1\n";

        var verdict = ArchiveExtractionPolicy.Evaluate(ArchiveListingParser.Parse(listing));

        Assert.False(verdict.IsSafe);
        Assert.Equal(ExtractionRejection.ParentTraversal, verdict.Rejection);
    }
}
