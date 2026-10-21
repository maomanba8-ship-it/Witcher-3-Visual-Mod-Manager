using System;
using Xunit;

namespace Witcher3ModManager.Tests;

/// <summary>
/// The test build and a release build must never share a data directory: settings written by one
/// would otherwise change the behaviour of the other, which is exactly how the Script Merger path
/// once leaked from the release build into the test build.
/// </summary>
public class AppDataPathTests
{
    [Fact]
    public void The_data_folder_follows_the_build_configuration()
    {
#if DEBUG
        Assert.Equal("Witcher3ModManager_Test", AppDataPath.FolderName);
#else
        Assert.Equal("Witcher3ModManager", AppDataPath.FolderName);
#endif
    }

    [Fact]
    public void Test_mode_matches_the_folder_name()
    {
        var looksLikeTestData = AppDataPath.FolderName.EndsWith("_Test", StringComparison.Ordinal);

        Assert.Equal(AppDataPath.IsTestMode, looksLikeTestData);
    }
}
