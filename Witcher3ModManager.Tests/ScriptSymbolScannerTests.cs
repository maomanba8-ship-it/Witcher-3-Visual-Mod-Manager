using System;
using System.IO;
using Witcher3ModManager.Core.Analysis.Script;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ScriptSymbolScannerTests
{
    [Fact]
    public void Loose_scripts_under_content_scripts_are_scanned()
    {
        var root = NewDir();
        try
        {
            var scripts = Path.Combine(root, "content", "scripts", "local");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(
                Path.Combine(scripts, "CleanScreenRemaster.ws"),
                "@wrapMethod( W3DamageManagerProcessor ) function ProcessActionReaction( a : bool, b : bool )");

            var result = ScriptSymbolScanner.ScanComponent(root);

            Assert.False(result.HasRsblob);
            Assert.Null(result.RsblobError);
            var symbol = Assert.Single(result.Symbols);
            Assert.Equal("W3DamageManagerProcessor", symbol.ClassName);
            Assert.Equal("ProcessActionReaction", symbol.MethodName);
            Assert.Equal(ScriptSymbolOrigin.LooseScript, symbol.Origin);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void An_unreadable_blob_is_reported_instead_of_thrown()
    {
        var root = NewDir();
        try
        {
            var content = Path.Combine(root, "content");
            Directory.CreateDirectory(content);
            File.WriteAllBytes(Path.Combine(content, "precompiled.rsblob"), new byte[] { 1, 2, 3 });

            var result = ScriptSymbolScanner.ScanComponent(root);

            Assert.True(result.HasRsblob);
            Assert.NotNull(result.RsblobError);
            Assert.Empty(result.Symbols);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_missing_component_root_yields_nothing()
    {
        var result = ScriptSymbolScanner.ScanComponent(Path.Combine(Path.GetTempPath(), "w3mm-missing-" + Guid.NewGuid()));

        Assert.Empty(result.Symbols);
        Assert.False(result.HasRsblob);
        Assert.Null(result.RsblobError);
    }

    [Fact]
    public void Fingerprint_matches_identical_content_and_changes_with_it()
    {
        var first = NewDir();
        var second = NewDir();
        var third = NewDir();
        try
        {
            Write(first, "@wrapMethod( CR4Player ) function OnSpawned( a : SEntitySpawnData )");
            Write(second, "@wrapMethod( CR4Player ) function OnSpawned( a : SEntitySpawnData )");
            Write(third, "@wrapMethod( CR4Player ) function OnSpawned( a : SEntitySpawnData ) // changed");

            var a = ScriptSymbolScanner.ScanComponent(first).Fingerprint;
            var b = ScriptSymbolScanner.ScanComponent(second).Fingerprint;
            var c = ScriptSymbolScanner.ScanComponent(third).Fingerprint;

            Assert.NotNull(a);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }
        finally
        {
            Directory.Delete(first, true);
            Directory.Delete(second, true);
            Directory.Delete(third, true);
        }
    }

    private static void Write(string root, string text)
    {
        var scripts = Path.Combine(root, "content", "scripts", "local");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "sample.ws"), text);
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w3mm-script-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
