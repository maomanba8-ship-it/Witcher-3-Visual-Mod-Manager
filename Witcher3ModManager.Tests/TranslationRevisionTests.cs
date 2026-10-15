using System.Collections.Generic;
using Witcher3ModManager.Core.Localization;
using Xunit;

namespace Witcher3ModManager.Tests;

public class TranslationRevisionTests
{
    [Fact]
    public void The_same_table_in_any_order_gives_the_same_revision()
    {
        var first = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var reordered = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };

        Assert.Equal(TranslationRevision.Compute(first), TranslationRevision.Compute(reordered));
    }

    [Fact]
    public void Changing_a_value_changes_the_revision()
    {
        var before = new Dictionary<string, string> { ["app.title"] = "Mod Manager v0.9.4" };
        var after = new Dictionary<string, string> { ["app.title"] = "Mod Manager v0.9.5" };

        // This is exactly the change that used to be invisible: the cached file kept winning.
        Assert.NotEqual(TranslationRevision.Compute(before), TranslationRevision.Compute(after));
    }

    [Fact]
    public void Adding_or_removing_a_key_changes_the_revision()
    {
        var before = new Dictionary<string, string> { ["a"] = "1" };
        var added = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };

        Assert.NotEqual(TranslationRevision.Compute(before), TranslationRevision.Compute(added));
    }

    [Fact]
    public void An_empty_table_has_no_revision()
    {
        Assert.Equal("", TranslationRevision.Compute(new Dictionary<string, string>()));
        Assert.Equal("", TranslationRevision.Compute(null));
    }
}
