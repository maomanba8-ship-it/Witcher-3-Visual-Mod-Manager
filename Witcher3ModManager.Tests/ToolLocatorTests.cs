using System;
using System.IO;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ToolLocatorTests
{
    [Fact]
    public void FirstExisting_prefers_the_first_candidate_that_exists()
    {
        var configured = CreateTempFile();
        var fallback = CreateTempFile();
        try
        {
            // The user's configured path must win over the built-in fallbacks.
            Assert.Equal(configured, ToolLocator.FirstExisting(new[] { configured, fallback }));
            Assert.Equal(fallback, ToolLocator.FirstExisting(new[] { configured + ".missing", fallback }));
        }
        finally
        {
            Cleanup(configured);
            Cleanup(fallback);
        }
    }

    [Fact]
    public void FirstExisting_skips_blank_and_missing_candidates()
    {
        var real = CreateTempFile();
        var missing = real + ".missing";
        try
        {
            Assert.Equal(real, ToolLocator.FirstExisting(new[] { null, "", "   ", missing, real }));
        }
        finally
        {
            Cleanup(real);
        }
    }

    [Fact]
    public void FirstExisting_returns_null_when_nothing_matches()
    {
        Assert.Null(ToolLocator.FirstExisting(null));
        Assert.Null(ToolLocator.FirstExisting(Array.Empty<string?>()));
        Assert.Null(ToolLocator.FirstExisting(new string?[] { null, "", "   " }));
        Assert.Null(ToolLocator.FirstExisting(new[] { @"C:\does-not-exist\tool.exe" }));
    }

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "w3mm_tool_" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        File.WriteAllText(path, "");
        return path;
    }

    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
