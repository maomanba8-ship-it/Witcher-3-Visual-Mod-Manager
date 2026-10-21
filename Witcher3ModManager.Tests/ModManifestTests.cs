using System;
using System.IO;
using Witcher3ModManager.Core.Analysis;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ModManifestTests : IDisposable
{
    private readonly string _root;

    public ModManifestTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "w3mm_manifest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "content"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* temp cleanup */ }
    }

    [Fact]
    public void A_v5_manifest_is_read_from_the_component_root()
    {
        Write("""
        {
            "name": "gwentfaithfullyeroticartwork",
            "modName": "gwentfaithfulartwork",
            "version": "1.0.0",
            "gameVersion": 29,
            "description": "Erotic edits",
            "author": "someone",
            "dependencies": ["baseMod"],
            "useLooseScripts": false
        }
        """);

        var manifest = Assert.IsType<ModManifestInfo>(ModManifest.Read(_root));
        Assert.Equal("gwentfaithfullyeroticartwork", manifest.Name);
        Assert.Equal("someone", manifest.Author);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("Erotic edits", manifest.Description);
        Assert.Equal(29, manifest.GameVersion);
        Assert.Equal(new[] { "baseMod" }, manifest.Dependencies);
    }

    [Fact]
    public void An_absent_manifest_is_simply_no_metadata()
    {
        Assert.Null(ModManifest.Read(_root));
        Assert.Null(ModManifest.Read(null));
        Assert.Null(ModManifest.Read(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void A_damaged_manifest_costs_metadata_and_nothing_else()
    {
        Write("{ this is not json");

        Assert.Null(ModManifest.Read(_root));
    }

    [Fact]
    public void Blank_and_mistyped_fields_become_null()
    {
        Write("""
        {
            "name": "   ",
            "author": 42,
            "version": null,
            "gameVersion": "29",
            "dependencies": ["ok", 3, ""]
        }
        """);

        var manifest = Assert.IsType<ModManifestInfo>(ModManifest.Read(_root));
        Assert.Null(manifest.Name);
        Assert.Null(manifest.Author);
        Assert.Null(manifest.Version);
        Assert.Null(manifest.GameVersion);
        Assert.Equal(new[] { "ok" }, manifest.Dependencies);
    }

    private void Write(string json) =>
        File.WriteAllText(Path.Combine(_root, "content", "info.json"), json);
}
