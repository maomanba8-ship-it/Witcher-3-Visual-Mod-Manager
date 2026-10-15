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

            Assert.NotNull(exe);
            Assert.Equal(Path.Combine(root, "bin", "x64_dx12", "witcher3.exe"), exe);
            Assert.True(GameLayout.IsGameRoot(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveGameExecutable_finds_the_classic_layout()
    {
        var root = CreateFakeGame("x64");
        try
        {
            var exe = GameLayout.ResolveGameExecutable(root);

            Assert.NotNull(exe);
            Assert.Equal(Path.Combine(root, "bin", "x64", "witcher3.exe"), exe);
            Assert.True(GameLayout.IsGameRoot(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveGameExecutable_prefers_the_newest_layout_when_both_exist()
    {
        var root = CreateFakeGame("x64_dx12");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
            File.WriteAllText(Path.Combine(root, "bin", "x64", "witcher3.exe"), "");

            var exe = GameLayout.ResolveGameExecutable(root);

            Assert.NotNull(exe);
            Assert.Contains("x64_dx12", exe!);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveGameExecutable_returns_null_when_there_is_no_game()
    {
        var empty = Path.Combine(Path.GetTempPath(), "w3mm_nogame_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Null(GameLayout.ResolveGameExecutable(empty));
            Assert.False(GameLayout.IsGameRoot(empty));
            Assert.Null(GameLayout.ResolveGameExecutable(null));
            Assert.Null(GameLayout.ResolveGameExecutable(""));
            Assert.Null(GameLayout.ResolveGameExecutable(
                Path.Combine(Path.GetTempPath(), "w3mm_missing_" + Guid.NewGuid().ToString("N")[..8])));
        }
        finally
        {
            if (Directory.Exists(empty)) Directory.Delete(empty, true);
        }
    }

    [Fact]
    public void GameRootFromBinPath_lifts_both_layouts_to_the_game_root()
    {
        var root = CreateFakeGame("x64_dx12");
        try
        {
            Assert.Equal(root, GameLayout.GameRootFromBinPath(Path.Combine(root, "bin", "x64_dx12")));
            Assert.Equal(root, GameLayout.GameRootFromBinPath(Path.Combine(root, "bin", "x64_dx12") + "\\"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        var classic = CreateFakeGame("x64");
        try
        {
            Assert.Equal(classic, GameLayout.GameRootFromBinPath(Path.Combine(classic, "bin", "x64")));
        }
        finally
        {
            if (Directory.Exists(classic)) Directory.Delete(classic, true);
        }
    }

    [Fact]
    public void GameRootFromBinPath_returns_null_for_a_root_or_unrelated_path()
    {
        Assert.Null(GameLayout.GameRootFromBinPath(@"C:\Games\The Witcher 3"));
        Assert.Null(GameLayout.GameRootFromBinPath(@"C:\Games\The Witcher 3\content"));
        Assert.Null(GameLayout.GameRootFromBinPath(null));
        Assert.Null(GameLayout.GameRootFromBinPath(""));
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
}
