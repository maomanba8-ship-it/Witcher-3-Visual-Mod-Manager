using Witcher3ModManager.Core.Analysis;
using Xunit;

namespace Witcher3ModManager.Tests;

public class BinOnlyPackageNameTests
{
    [Fact]
    public void The_folder_around_bin_is_the_name()
    {
        var name = BinOnlyPackageName.From("BetterReflections", "_tmp_255a4158", "RT-mod-1234");

        Assert.Equal("BetterReflections", name);
    }

    [Fact]
    public void An_archive_that_unpacks_bin_at_the_top_level_is_named_after_the_archive()
    {
        // "bin/" directly inside the extraction folder: there is no mod folder to borrow a name
        // from, so the archive's name is used rather than this manager's temporary folder.
        var name = BinOnlyPackageName.From("_tmp_255a4158", "_tmp_255a4158", "RT_ImprovedReflectionsAndShadows");

        Assert.Equal("RT_ImprovedReflectionsAndShadows", name);
    }

    [Fact]
    public void Without_a_parent_folder_the_archive_names_it()
    {
        Assert.Equal("SomeMod", BinOnlyPackageName.From(null, "_tmp_abc12345", "SomeMod"));
        Assert.Equal("SomeMod", BinOnlyPackageName.From("   ", "_tmp_abc12345", "SomeMod"));
    }

    [Fact]
    public void A_temporary_folder_is_never_preferred_over_a_real_name()
    {
        Assert.DoesNotContain("_tmp_", BinOnlyPackageName.From("_tmp_255a4158", "_tmp_255a4158", "RealName"));
        Assert.DoesNotContain("_tmp_", BinOnlyPackageName.From(null, "_tmp_255a4158", "RealName"));
    }

    [Fact]
    public void The_temporary_folder_is_a_last_resort_rather_than_an_empty_name()
    {
        Assert.Equal("_tmp_255a4158", BinOnlyPackageName.From("_tmp_255a4158", "_tmp_255a4158", ""));
        Assert.Equal("_tmp_255a4158", BinOnlyPackageName.From(null, "_tmp_255a4158", null));
    }

    [Fact]
    public void Names_are_trimmed_and_case_does_not_confuse_the_comparison()
    {
        Assert.Equal("BetterReflections", BinOnlyPackageName.From("  BetterReflections  ", "_tmp_x", "other"));
        Assert.Equal("ArchiveName", BinOnlyPackageName.From("_TMP_255A4158", "_tmp_255a4158", "ArchiveName"));
    }
}
