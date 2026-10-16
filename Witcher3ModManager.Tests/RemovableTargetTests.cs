using System;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class RemovableTargetTests
{
    private static readonly string[] Roots =
    {
        @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\mods",
        @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\bin",
        @"Z:\game\mods",
        @"Z:\game\dlc",
        @"Z:\game"
    };

    [Fact]
    public void A_package_folder_inside_the_storage_root_is_removable()
    {
        Assert.True(RemovableTarget.IsComponent(
            @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\mods\modMagicSpells", Roots));
    }

    [Fact]
    public void The_storage_root_itself_is_not_removable()
    {
        // This is the path a bin-only package carries in InstalledPath.
        Assert.False(RemovableTarget.IsComponent(
            @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\mods", Roots));
    }

    [Fact]
    public void The_game_directories_are_not_removable_either()
    {
        Assert.False(RemovableTarget.IsComponent(@"Z:\game\mods", Roots));
        Assert.False(RemovableTarget.IsComponent(@"Z:\game\dlc", Roots));
        Assert.False(RemovableTarget.IsComponent(@"Z:\game", Roots));
        Assert.False(RemovableTarget.IsComponent(
            @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\bin", Roots));
    }

    [Fact]
    public void Spelling_the_same_path_differently_still_matches_a_root()
    {
        Assert.False(RemovableTarget.IsComponent(
            @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\mods\", Roots));
        Assert.False(RemovableTarget.IsComponent(
            @"c:\users\rukawa\appdata\local\witcher3modmanager_test\MODS", Roots));
        Assert.False(RemovableTarget.IsComponent(
            @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\mods\sub\..", Roots));
    }

    [Fact]
    public void An_empty_or_unusable_path_is_never_removable()
    {
        Assert.False(RemovableTarget.IsComponent(null, Roots));
        Assert.False(RemovableTarget.IsComponent("", Roots));
        Assert.False(RemovableTarget.IsComponent("   ", Roots));
        // Path.Combine(root, "") is the root, and that case has to be caught as well.
        Assert.False(RemovableTarget.IsComponent(
            System.IO.Path.Combine(@"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\mods", ""), Roots));
    }

    [Fact]
    public void A_folder_whose_name_happens_to_match_a_root_name_is_still_removable()
    {
        Assert.True(RemovableTarget.IsComponent(
            @"C:\Users\Rukawa\AppData\Local\Witcher3ModManager_Test\mods\mods", Roots));
    }

    [Fact]
    public void Without_any_roots_every_real_path_is_a_component()
    {
        Assert.True(RemovableTarget.IsComponent(@"C:\anything\here", null));
        Assert.False(RemovableTarget.IsComponent("", null));
    }
}
