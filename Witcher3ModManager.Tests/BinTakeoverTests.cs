using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class BinTakeoverTests
{
    [Fact]
    public void Nothing_existing_records_nothing()
    {
        Assert.Null(BinTakeover.OriginalToRecord(null, "new"));
    }

    [Fact]
    public void Identical_content_is_treated_as_ours()
    {
        // The file already holds exactly what this package ships (a hand install or a re-enable).
        // Disabling must remove it, not restore a copy of itself.
        Assert.Null(BinTakeover.OriginalToRecord("ABC123", "abc123"));
    }

    [Fact]
    public void Different_content_is_remembered_as_the_original()
    {
        Assert.Equal("OLD", BinTakeover.OriginalToRecord("OLD", "NEW"));
    }

    [Fact]
    public void Unknown_incoming_hash_keeps_the_original()
    {
        // "Cannot read the package file" must never turn into a delete on disable.
        Assert.Equal("OLD", BinTakeover.OriginalToRecord("OLD", null));
        Assert.Equal("OLD", BinTakeover.OriginalToRecord("OLD", ""));
    }
}
