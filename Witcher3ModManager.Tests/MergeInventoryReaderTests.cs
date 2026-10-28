using System;
using System.IO;
using Witcher3ModManager.Core.Analysis.Merge;
using Xunit;

namespace Witcher3ModManager.Tests;

public class MergeInventoryReaderTests
{
    [Fact]
    public void Reads_entries_from_script_merger_inventory()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, MergeInventoryReader.FileName);
            File.WriteAllText(path, """
                <?xml version="1.0" encoding="utf-8"?>
                <MergeInventory xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <Merge>
                    <RelativePath>game\player\r4Player.ws</RelativePath>
                    <IncludedMod Hash="7B7DCF65" ModIoId="0">modNoFallDamage</IncludedMod>
                    <IncludedMod Hash="9D8684F7" ModIoId="0">modJumpInShallowWater</IncludedMod>
                    <MergedModName>mod0000_MergedFiles</MergedModName>
                  </Merge>
                </MergeInventory>
                """);

            var entry = Assert.Single(MergeInventoryReader.Read(path));

            Assert.Equal("content/scripts/game/player/r4player.ws", entry.ConflictPath);
            Assert.Equal("mod0000_MergedFiles", entry.MergedModName);
            Assert.Equal(new[] { "modNoFallDamage", "modJumpInShallowWater" }, entry.IncludedMods);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Missing_or_malformed_inventory_yields_nothing()
    {
        var dir = NewDir();
        try
        {
            Assert.Empty(MergeInventoryReader.Read(Path.Combine(dir, "missing.xml")));

            var path = Path.Combine(dir, MergeInventoryReader.FileName);
            File.WriteAllText(path, "<MergeInventory><Merge>");

            Assert.Empty(MergeInventoryReader.Read(path));
            Assert.Empty(MergeInventoryReader.ReadFromScriptMerger(null));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Entries_without_a_path_or_a_merged_mod_are_skipped()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, MergeInventoryReader.FileName);
            File.WriteAllText(path, """
                <MergeInventory>
                  <Merge>
                    <RelativePath>game\player\r4Player.ws</RelativePath>
                  </Merge>
                  <Merge>
                    <MergedModName>mod0000_MergedFiles</MergedModName>
                  </Merge>
                </MergeInventory>
                """);

            Assert.Empty(MergeInventoryReader.Read(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w3mm-merge-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
