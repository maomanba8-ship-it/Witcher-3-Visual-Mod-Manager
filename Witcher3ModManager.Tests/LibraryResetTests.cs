using System;
using System.IO;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class LibraryResetTests : IDisposable
{
    private readonly string _root;
    private static readonly DateTime When = new(2026, 10, 21, 9, 30, 0);

    public LibraryResetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "w3mm_reset_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* temp cleanup */ }
    }

    [Fact]
    public void An_existing_file_is_kept_beside_itself_before_a_reset()
    {
        var file = Write("mods.json", "{\"mods\":[1]}");

        var (copies, problems) = LibraryReset.Preserve(new[] { file }, When);

        Assert.Empty(problems);
        var copy = Assert.Single(copies);
        Assert.Contains("mods.json" + LibraryReset.PreservedSuffix + "20261021-093000", copy);
        Assert.Equal("{\"mods\":[1]}", File.ReadAllText(copy));
        // The original is untouched: preserving is not the reset itself.
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Files_that_are_not_there_are_skipped()
    {
        var (copies, problems) = LibraryReset.Preserve(new[] { Path.Combine(_root, "missing.json") }, When);

        Assert.Empty(copies);
        Assert.Empty(problems);
    }

    [Fact]
    public void Blank_entries_are_ignored()
    {
        var (copies, problems) = LibraryReset.Preserve(new[] { null, "", "   " }, When);

        Assert.Empty(copies);
        Assert.Empty(problems);
    }

    [Fact]
    public void Every_file_is_preserved_independently()
    {
        var a = Write("mods.json", "a");
        var b = Write("library.json", "b");

        var (copies, problems) = LibraryReset.Preserve(new[] { a, b }, When);

        Assert.Equal(2, copies.Count);
        Assert.Empty(problems);
        Assert.All(copies, c => Assert.True(File.Exists(c)));
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
}
