using System;
using System.IO;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Manifest;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ComponentFingerprintCollectorTests
{
    [Fact]
    public void FromRoot_records_domain_folder_file_count_and_fingerprint()
    {
        var root = CreateTempComponentRoot(("content/a.txt", "AAA"), ("scripts/x.ws", "SCRIPT"));

        try
        {
            var entry = ComponentFingerprintCollector.FromRoot(TargetDomain.Dlc, "dlcFoo", root);

            Assert.NotNull(entry);
            Assert.Equal(TargetDomain.Dlc, entry!.Domain);
            Assert.Equal("dlcFoo", entry.Folder);
            Assert.Equal(2, entry.FileCount);
            Assert.Equal(
                ComponentFingerprint.Compute(ManifestBuilder.Build(root, "dlcFoo", TargetDomain.Dlc)),
                entry.Fingerprint);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Identical_content_in_different_folders_shares_a_fingerprint()
    {
        var first = CreateTempComponentRoot(("content/shared.txt", "SAME"));
        var second = CreateTempComponentRoot(("content/shared.txt", "SAME"));

        try
        {
            var owned = ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "mod_EH_RiderUtil", first);
            var other = ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "mod_EH_RiderUtil", second);

            Assert.NotNull(owned);
            Assert.NotNull(other);
            Assert.False(ComponentFingerprint.Differs(owned!.Fingerprint, other!.Fingerprint));
        }
        finally
        {
            if (Directory.Exists(first)) Directory.Delete(first, true);
            if (Directory.Exists(second)) Directory.Delete(second, true);
        }
    }

    [Fact]
    public void Same_folder_with_different_content_differs()
    {
        var first = CreateTempComponentRoot(("content/shared.txt", "OLD"));
        var second = CreateTempComponentRoot(("content/shared.txt", "NEW"));

        try
        {
            var before = ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "modShared", first);
            var after = ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "modShared", second);

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.True(ComponentFingerprint.Differs(before!.Fingerprint, after!.Fingerprint));
        }
        finally
        {
            if (Directory.Exists(first)) Directory.Delete(first, true);
            if (Directory.Exists(second)) Directory.Delete(second, true);
        }
    }

    [Fact]
    public void FromRoot_returns_null_when_input_is_unusable()
    {
        var missing = Path.Combine(Path.GetTempPath(), "w3mm_missing_" + Guid.NewGuid().ToString("N")[..8]);

        Assert.Null(ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "modFoo", null));
        Assert.Null(ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "modFoo", ""));
        Assert.Null(ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "modFoo", missing));
        Assert.Null(ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, null, missing));
        Assert.Null(ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "", missing));
    }

    [Fact]
    public void Component_root_without_files_is_not_recorded()
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_empty_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            Assert.Null(ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "modEmpty", root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Fingerprint_entry_survives_a_json_round_trip()
    {
        var root = CreateTempComponentRoot(("content/a.txt", "AAA"));

        try
        {
            var entry = ComponentFingerprintCollector.FromRoot(TargetDomain.Mods, "modFoo", root);
            Assert.NotNull(entry);

            var json = System.Text.Json.JsonSerializer.Serialize(
                new List<ComponentFingerprintEntry> { entry! });
            var restored = System.Text.Json.JsonSerializer.Deserialize<List<ComponentFingerprintEntry>>(json);

            Assert.NotNull(restored);
            Assert.Single(restored!);
            Assert.Equal(entry, restored![0]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string CreateTempComponentRoot(params (string Rel, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_fp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }
}
