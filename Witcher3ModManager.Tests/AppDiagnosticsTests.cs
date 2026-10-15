using System.IO;
using Witcher3ModManager.Core.Diagnostics;
using Xunit;

namespace Witcher3ModManager.Tests;

public class AppDiagnosticsTests
{
    [Fact]
    public void LogFilePath_nests_the_log_under_the_given_data_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_data");

        var path = AppDiagnostics.LogFilePath(root);

        Assert.Equal(Path.Combine(root, "logs", "diagnostics.log"), path);
        Assert.StartsWith(root, path);
    }

    [Fact]
    public void LogFilePath_never_returns_a_rooted_path_for_a_blank_root()
    {
        Assert.Equal("diagnostics.log", AppDiagnostics.LogFilePath(null));
        Assert.Equal("diagnostics.log", AppDiagnostics.LogFilePath(""));
        Assert.Equal("diagnostics.log", AppDiagnostics.LogFilePath("   "));
    }
}
