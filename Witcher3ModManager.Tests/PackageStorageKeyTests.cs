using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class PackageStorageKeyTests
{
    [Fact]
    public void A_record_with_an_id_is_stored_under_its_id()
    {
        Assert.Equal("b3c13053bd86", PackageStorageKey.For("b3c13053bd86", "Magic Spells"));
    }

    [Fact]
    public void Renaming_the_display_name_does_not_move_the_copies()
    {
        Assert.Equal(PackageStorageKey.For("abc123", "Old Name"), PackageStorageKey.For("abc123", "New Name"));
    }

    [Fact]
    public void Without_an_id_the_name_is_the_fallback()
    {
        Assert.Equal("Some Mod", PackageStorageKey.For(null, "Some Mod"));
        Assert.Equal("Some Mod", PackageStorageKey.For("   ", "  Some Mod  "));
    }

    [Fact]
    public void An_empty_record_still_gets_a_usable_folder_name()
    {
        Assert.Equal("unknown", PackageStorageKey.For(null, null));
        Assert.Equal("unknown", PackageStorageKey.For("", ""));
    }
}
