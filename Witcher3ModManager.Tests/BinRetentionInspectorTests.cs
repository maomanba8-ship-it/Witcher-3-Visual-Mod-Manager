using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class BinRetentionInspectorTests
{
    [Fact]
    public void A_disabled_mod_whose_bin_file_is_still_present_is_reported()
    {
        var claims = new[] { new BinClaim("modA", IsEnabled: false, new[] { "config/a.xml" }) };

        var retained = BinRetentionInspector.FindRetained(claims, f => f == "config/a.xml");

        var entry = Assert.Single(retained);
        Assert.Equal("modA", entry.ModName);
        Assert.Equal(new[] { "config/a.xml" }, entry.RetainedFiles);
    }

    [Fact]
    public void A_disabled_mod_whose_bin_file_is_gone_is_not_reported()
    {
        var claims = new[] { new BinClaim("modA", IsEnabled: false, new[] { "config/a.xml" }) };

        Assert.Empty(BinRetentionInspector.FindRetained(claims, _ => false));
    }

    [Fact]
    public void An_enabled_mod_is_never_reported_as_retained()
    {
        var claims = new[] { new BinClaim("modA", IsEnabled: true, new[] { "config/a.xml" }) };

        Assert.Empty(BinRetentionInspector.FindRetained(claims, _ => true));
    }

    [Fact]
    public void Only_the_files_that_are_actually_present_are_listed()
    {
        var claims = new[]
        {
            new BinClaim("modA", IsEnabled: false, new[] { "config/gone.xml", "config/here.xml" })
        };

        var entry = Assert.Single(BinRetentionInspector.FindRetained(claims, f => f.EndsWith("here.xml")));

        Assert.Equal(new[] { "config/here.xml" }, entry.RetainedFiles);
    }

    [Fact]
    public void Blank_and_duplicate_entries_are_skipped()
    {
        var claims = new[]
        {
            new BinClaim("modA", IsEnabled: false, new[] { "", "   ", "config/a.xml", "CONFIG/A.XML" })
        };

        var entry = Assert.Single(BinRetentionInspector.FindRetained(claims, _ => true));

        Assert.Single(entry.RetainedFiles);
    }

    [Fact]
    public void Mods_without_bin_files_are_ignored()
    {
        var claims = new[]
        {
            new BinClaim("modA", IsEnabled: false, new List<string>()),
            new BinClaim("modB", IsEnabled: false, null!)
        };

        Assert.Empty(BinRetentionInspector.FindRetained(claims, _ => true));
    }

    [Fact]
    public void Results_are_sorted_by_mod_name_and_only_retaining_mods_appear()
    {
        var claims = new[]
        {
            new BinClaim("modZ", IsEnabled: false, new[] { "z.xml" }),
            new BinClaim("modA", IsEnabled: false, new[] { "a.xml" }),
            new BinClaim("modM", IsEnabled: false, new[] { "missing.xml" })
        };

        var retained = BinRetentionInspector.FindRetained(claims, f => !f.StartsWith("missing"));

        Assert.Equal(new[] { "modA", "modZ" }, retained.Select(r => r.ModName).ToArray());
    }

    [Fact]
    public void Missing_inputs_yield_nothing()
    {
        Assert.Empty(BinRetentionInspector.FindRetained(null, _ => true));
        Assert.Empty(BinRetentionInspector.FindRetained(
            new[] { new BinClaim("modA", IsEnabled: false, new[] { "a.xml" }) }, null));
    }
}
