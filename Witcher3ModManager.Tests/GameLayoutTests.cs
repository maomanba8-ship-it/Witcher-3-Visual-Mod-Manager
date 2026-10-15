using System;
using System.IO;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class GameLayoutTests
{
    [Fact]
    public void ResolveGameExecutable_finds_the_dx12_only_layout()
    {
        // The newer build ships bin\x64_dx12 and no bin\x64 at all.
        var root = CreateFakeGame("x64_dx12");
        try
        {
            var exe = GameLayout.ResolveGameExecutable(root);

            Assert.Equal(Path.Combine(root, "bin", "x64_dx12", "witcher3.exe"), exe);
            Assert.True(GameLayout.IsGameRoot(root));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ResolveGameExecutable_finds_the_classic_layout()
    {
        var root = CreateFakeGame("x64");
        try
        {
            Assert.Equal(Path.Combine(root, "bin", "x64", "witcher3.exe"),
                GameLayout.ResolveGameExecutable(root));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ResolveGameExecutable_finds_a_bin_subfolder_it_has_never_heard_of()
    {
        // The point of the structural search: a future layout rename must not need a code change.
        var root = CreateFakeGame("x64_vulkan_nextgen");
        try
        {
            Assert.Equal(Path.Combine(root, "bin", "x64_vulkan_nextgen", "witcher3.exe"),
                GameLayout.ResolveGameExecutable(root));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ResolveGameExecutable_finds_the_executable_directly_inside_bin()
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_game_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(root, "bin", "witcher3.exe"), "");
        try
        {
            Assert.Equal(Path.Combine(root, "bin", "witcher3.exe"), GameLayout.ResolveGameExecutable(root));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ResolveGameExecutable_prefers_the_newest_known_naming_when_both_exist()
    {
        var root = CreateFakeGame("x64_dx12");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
            File.WriteAllText(Path.Combine(root, "bin", "x64", "witcher3.exe"), "");

            Assert.Contains("x64_dx12", GameLayout.ResolveGameExecutable(root)!);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ResolveGameExecutable_accepts_root_bin_subfolder_or_the_exe_itself()
    {
        var root = CreateFakeGame("x64_dx12");
        var exe = Path.Combine(root, "bin", "x64_dx12", "witcher3.exe");
        try
        {
            Assert.Equal(exe, GameLayout.ResolveGameExecutable(root));
            Assert.Equal(exe, GameLayout.ResolveGameExecutable(Path.Combine(root, "bin")));
            Assert.Equal(exe, GameLayout.ResolveGameExecutable(Path.Combine(root, "bin", "x64_dx12")));
            Assert.Equal(exe, GameLayout.ResolveGameExecutable(Path.Combine(root, "bin", "x64_dx12") + "\\"));
            Assert.Equal(exe, GameLayout.ResolveGameExecutable(exe));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void FindGameRoot_walks_up_from_a_folder_inside_the_installation()
    {
        var root = CreateFakeGame("x64_dx12");
        try
        {
            Assert.Equal(root, GameLayout.FindGameRoot(root));
            Assert.Equal(root, GameLayout.FindGameRoot(Path.Combine(root, "bin", "x64_dx12")));
            Assert.Equal(root, GameLayout.FindGameRoot(Path.Combine(root, "mods", "modFoo")));
            Assert.Equal(root, GameLayout.FindGameRoot(Path.Combine(root, "dlc", "dlcFoo")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void FindGameRoot_returns_null_when_no_ancestor_holds_the_game()
    {
        // Nested deeper than the root search reaches, so the walk stays inside this temp tree and
        // the assertion does not depend on whatever happens to live above the temp directory.
        var outer = Path.Combine(Path.GetTempPath(), "w3mm_nogame_" + Guid.NewGuid().ToString("N")[..8]);
        var deep = Path.Combine(outer, "lvl1", "lvl2", "lvl3", "lvl4", "lvl5");
        Directory.CreateDirectory(deep);
        try
        {
            Assert.Null(GameLayout.FindGameRoot(deep));
            Assert.Null(GameLayout.ResolveGameExecutable(deep));
        }
        finally
        {
            Cleanup(outer);
        }
    }

    [Fact]
    public void IsGameRoot_is_strict_about_being_the_root()
    {
        var root = CreateFakeGame("x64_dx12");
        try
        {
            Assert.True(GameLayout.IsGameRoot(root));
            Assert.False(GameLayout.IsGameRoot(Path.Combine(root, "bin")));
            Assert.False(GameLayout.IsGameRoot(Path.Combine(root, "bin", "x64_dx12")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Everything_returns_null_for_blank_or_missing_paths()
    {
        Assert.Null(GameLayout.ResolveGameExecutable(null));
        Assert.Null(GameLayout.ResolveGameExecutable(""));
        Assert.Null(GameLayout.ResolveGameExecutable("   "));
        Assert.Null(GameLayout.ResolveGameExecutable(
            Path.Combine(Path.GetTempPath(), "w3mm_missing_" + Guid.NewGuid().ToString("N")[..8])));
        Assert.False(GameLayout.IsGameRoot(null));
        Assert.False(GameLayout.IsGameRoot(""));
    }

    private static string CreateFakeGame(string binSubfolder)
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_game_" + Guid.NewGuid().ToString("N")[..8]);
        var binDir = Path.Combine(root, "bin", binSubfolder);
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "witcher3.exe"), "");
        Directory.CreateDirectory(Path.Combine(root, "content"));
        return root;
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
