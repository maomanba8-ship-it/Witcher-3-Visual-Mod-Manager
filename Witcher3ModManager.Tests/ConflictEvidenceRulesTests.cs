using System.Collections.Generic;
using Witcher3ModManager.Core.Analysis;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ConflictEvidenceRulesTests
{
    public static IEnumerable<object[]> UserFacingKinds()
    {
        yield return new object[] { ConflictKind.DefiniteOverride, true };
        yield return new object[] { ConflictKind.ScriptOverride, true };
        yield return new object[] { ConflictKind.BinOverrideRisk, true };
        yield return new object[] { ConflictKind.UnsupportedContainer, true };
        yield return new object[] { ConflictKind.CategorySimilarity, true };
        yield return new object[] { ConflictKind.DuplicateContent, false };
    }

    [Theory]
    [MemberData(nameof(UserFacingKinds))]
    public void IsUserFacing_matches_expected(ConflictKind kind, bool expected)
    {
        Assert.Equal(expected, ConflictEvidenceRules.IsUserFacing(kind));
    }

    [Theory]
    [InlineData(ConflictKind.ScriptOverride, true)]
    [InlineData(ConflictKind.DefiniteOverride, false)]
    [InlineData(ConflictKind.DuplicateContent, false)]
    [InlineData(ConflictKind.UnsupportedContainer, false)]
    public void IsScript_matches_expected(ConflictKind kind, bool expected)
    {
        Assert.Equal(expected, ConflictEvidenceRules.IsScript(kind));
    }
}