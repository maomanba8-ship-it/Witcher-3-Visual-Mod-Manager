using System;
using System.IO;
using Witcher3ModManager.Core.Import;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ManualInstallRootTests : IDisposable
{
    private readonly string _root;

    public ManualInstallRootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "w3mm_manual_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* temp cleanup */ }
    }

    [Fact]
    public void A_single_wrapper_folder_is_the_package()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Some Mod", "content"));

        Assert.Equal(Path.Combine(_root, "Some Mod"), ManualInstallRoot.From(_root));
    }

    [Fact]
    public void A_wrapper_named_after_a_marker_is_not_a_wrapper()
    {
        // content/ at the top level must keep its name, so the extraction root is installed.
        Directory.CreateDirectory(Path.Combine(_root, "content", "scripts"));

        Assert.Equal(_root, ManualInstallRoot.From(_root));
    }

    [Fact]
    public void Extra_files_at_the_top_level_mean_the_whole_tree_is_the_package()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Some Mod", "content"));
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "hello");

        Assert.Equal(_root, ManualInstallRoot.From(_root));
    }

    [Fact]
    public void A_tree_with_only_files_is_installed_as_the_root()
    {
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "hello");

        Assert.Equal(_root, ManualInstallRoot.From(_root));
    }

    [Fact]
    public void An_empty_or_missing_tree_has_nothing_to_install()
    {
        Assert.Null(ManualInstallRoot.From(_root));
        Assert.Null(ManualInstallRoot.From(Path.Combine(_root, "missing")));
        Assert.Null(ManualInstallRoot.From(null));
        Assert.Null(ManualInstallRoot.From("   "));
    }

    [Fact]
    public void Structural_marker_names_are_recognised_case_insensitively()
    {
        Assert.True(ManualInstallRoot.IsStructuralMarker("Content"));
        Assert.True(ManualInstallRoot.IsStructuralMarker("BIN"));
        Assert.False(ManualInstallRoot.IsStructuralMarker("mods"));
    }
}
