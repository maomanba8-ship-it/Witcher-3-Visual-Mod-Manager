using System;
using System.IO;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class GameFolderShapeTests : IDisposable
{
    private readonly string _root;

    public GameFolderShapeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "w3mm_shape_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* temp cleanup */ }
    }

    [Fact]
    public void A_folder_with_content_is_a_mod_folder()
    {
        var folder = Make("modA", "content/scripts");

        Assert.True(GameFolderShape.IsModFolder(folder));
    }

    [Fact]
    public void A_folder_that_only_ships_bin_files_is_a_mod_folder_too()
    {
        // This is the shape that used to be missed entirely.
        var folder = Make("modBinOnly", "bin/config");
        File.WriteAllText(Path.Combine(folder, "bin", "config", "input.xml"), "x");

        Assert.True(GameFolderShape.IsModFolder(folder));
    }

    [Fact]
    public void An_empty_bin_folder_is_not_a_payload()
    {
        var folder = Make("notAMod", "bin");

        Assert.False(GameFolderShape.IsModFolder(folder));
    }

    [Fact]
    public void A_folder_with_neither_shape_is_not_a_mod_folder()
    {
        Assert.False(GameFolderShape.IsModFolder(Make("readme", "docs")));
    }

    [Fact]
    public void Something_that_is_not_there_is_not_a_mod_folder()
    {
        Assert.False(GameFolderShape.IsModFolder(Path.Combine(_root, "missing")));
        Assert.False(GameFolderShape.IsModFolder(null));
        Assert.False(GameFolderShape.IsModFolder("   "));
    }

    /// <summary>Creates a folder and a nested one inside it, and returns it.</summary>
    private string Make(string name, string nested)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, nested.Replace('/', Path.DirectorySeparatorChar)));
        return folder;
    }
}
