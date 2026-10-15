using System.Collections.Generic;
using Witcher3ModManager.Core.Shared;
using Xunit;

namespace Witcher3ModManager.Tests;

public class SharedFingerprintComparisonTests
{
    [Fact]
    public void Owners_with_equal_fingerprints_are_identical()
    {
        Assert.Equal(
            SharedContentVerdict.Identical,
            SharedFingerprintComparison.Compare(new[] { "abc", "abc" }));
    }

    [Fact]
    public void Owners_with_different_fingerprints_differ()
    {
        Assert.Equal(
            SharedContentVerdict.Differs,
            SharedFingerprintComparison.Compare(new[] { "abc", "xyz" }));
    }

    [Fact]
    public void A_missing_record_makes_the_verdict_unknown()
    {
        Assert.Equal(
            SharedContentVerdict.Unknown,
            SharedFingerprintComparison.Compare(new[] { "abc", null }));
        Assert.Equal(
            SharedContentVerdict.Unknown,
            SharedFingerprintComparison.Compare(new[] { "abc", "" }));
    }

    [Fact]
    public void Unknown_history_is_never_reported_as_a_difference()
    {
        var verdict = SharedFingerprintComparison.Compare(new[] { "abc", null, "xyz" });

        Assert.NotEqual(SharedContentVerdict.Differs, verdict);
    }

    [Fact]
    public void A_single_owner_is_not_a_shared_folder()
    {
        Assert.Equal(
            SharedContentVerdict.Unknown,
            SharedFingerprintComparison.Compare(new[] { "abc" }));
        Assert.Equal(
            SharedContentVerdict.Unknown,
            SharedFingerprintComparison.Compare(new List<string?>()));
        Assert.Equal(
            SharedContentVerdict.Unknown,
            SharedFingerprintComparison.Compare(null));
    }

    [Fact]
    public void Comparison_ignores_fingerprint_case()
    {
        Assert.Equal(
            SharedContentVerdict.Identical,
            SharedFingerprintComparison.Compare(new[] { "ABC", "abc" }));
    }
}
