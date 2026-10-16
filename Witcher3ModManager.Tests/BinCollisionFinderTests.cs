using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Import;
using Xunit;

namespace Witcher3ModManager.Tests;

public class BinCollisionFinderTests
{
    private const string InputXml = "config/r4game/user_config_matrix/pc/input.xml";
    private const string ReflectionsIni = "config/platform/pc/RT_ImprovedReflectionsAndShadows.ini";

    [Fact]
    public void A_planned_file_an_installed_package_deploys_is_a_collision()
    {
        var collisions = BinCollisionFinder.Find(
            new[] { InputXml },
            new[] { ("BetterCombat", (IEnumerable<string>)new[] { InputXml }) });

        var collision = Assert.Single(collisions);
        Assert.Equal(InputXml, collision.Path);
        Assert.Equal(new[] { "BetterCombat" }, collision.Owners);
    }

    [Fact]
    public void Different_files_do_not_collide()
    {
        var collisions = BinCollisionFinder.Find(
            new[] { InputXml },
            new[] { ("ReflectionsMod", (IEnumerable<string>)new[] { ReflectionsIni }) });

        Assert.Empty(collisions);
    }

    [Fact]
    public void Separators_and_case_do_not_hide_a_collision()
    {
        var collisions = BinCollisionFinder.Find(
            new[] { @"config\r4game\user_config_matrix\pc\input.xml" },
            new[] { ("BetterCombat", (IEnumerable<string>)new[] { "CONFIG/r4game/User_Config_Matrix/pc/Input.XML" }) });

        Assert.Single(collisions);
    }

    [Fact]
    public void Both_owners_are_named()
    {
        var collisions = BinCollisionFinder.Find(
            new[] { InputXml },
            new[]
            {
                ("BetterCombat", (IEnumerable<string>)new[] { InputXml }),
                ("CombatTweaks", (IEnumerable<string>)new[] { InputXml })
            });

        Assert.Equal(new[] { "BetterCombat", "CombatTweaks" }, Assert.Single(collisions).Owners);
    }

    [Fact]
    public void A_file_listed_twice_is_reported_once_and_blanks_are_ignored()
    {
        var collisions = BinCollisionFinder.Find(
            new[] { InputXml, InputXml, "", "   " },
            new[] { ("BetterCombat", (IEnumerable<string>)new[] { InputXml }) });

        Assert.Single(collisions);
    }

    [Fact]
    public void Nothing_planned_or_nothing_installed_means_no_collisions()
    {
        Assert.Empty(BinCollisionFinder.Find(null, new[] { ("A", (IEnumerable<string>)new[] { InputXml }) }));
        Assert.Empty(BinCollisionFinder.Find(new[] { InputXml }, null));
        Assert.Empty(BinCollisionFinder.Find(new[] { InputXml }, new[] { ("A", (IEnumerable<string>?)null) }));
    }

    [Fact]
    public void Collisions_are_reported_in_path_order()
    {
        var collisions = BinCollisionFinder.Find(
            new[] { ReflectionsIni, InputXml },
            new[]
            {
                ("Other", (IEnumerable<string>)new[] { InputXml, ReflectionsIni })
            });

        Assert.Equal(collisions.Select(c => c.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
            collisions.Select(c => c.Path));
    }
}
