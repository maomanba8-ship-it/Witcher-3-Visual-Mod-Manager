using System.Collections.Generic;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Manifest;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ComponentFingerprintTests
{
    private static ComponentFile File(string identity, string path, string hash) =>
        new(TargetDomain.Mods, identity, path, hash, 1);

    [Fact]
    public void Same_files_in_any_order_share_a_fingerprint()
    {
        var first = new List<ComponentFile>
        {
            File("modA", "content/a.txt", "shaA"),
            File("modA", "content/b.txt", "shaB"),
        };
        var reversed = new List<ComponentFile>
        {
            File("modA", "content/b.txt", "shaB"),
            File("modA", "content/a.txt", "shaA"),
        };

        Assert.Equal(ComponentFingerprint.Compute(first), ComponentFingerprint.Compute(reversed));
    }

    [Fact]
    public void Different_hash_changes_the_fingerprint()
    {
        var before = new[] { File("modA", "content/a.txt", "shaA") };
        var after = new[] { File("modA", "content/a.txt", "shaB") };

        Assert.True(ComponentFingerprint.Differs(
            ComponentFingerprint.Compute(before),
            ComponentFingerprint.Compute(after)));
    }

    [Fact]
    public void Path_normalization_does_not_change_the_fingerprint()
    {
        var canonical = new[] { File("modA", "content/a.txt", "shaA") };
        var messy = new[] { File("modA", "content\\A.TXT", "shaA") };
        var dotted = new[] { File("modA", "./content/a.txt", "shaA") };

        Assert.Equal(ComponentFingerprint.Compute(canonical), ComponentFingerprint.Compute(messy));
        Assert.Equal(ComponentFingerprint.Compute(canonical), ComponentFingerprint.Compute(dotted));
    }

    [Fact]
    public void Fingerprint_ignores_component_identity_and_domain()
    {
        var owned = new[] { File("modA", "content/a.txt", "shaA") };
        var sharedByAnother = new[]
        {
            new ComponentFile(TargetDomain.Dlc, "dlcB", "content/a.txt", "shaA", 1)
        };

        Assert.Equal(ComponentFingerprint.Compute(owned), ComponentFingerprint.Compute(sharedByAnother));
    }

    [Fact]
    public void Duplicate_entries_do_not_change_the_fingerprint()
    {
        var once = new[] { File("modA", "content/a.txt", "shaA") };
        var twice = new[] { File("modA", "content/a.txt", "shaA"), File("modA", "content/a.txt", "shaA") };

        Assert.Equal(ComponentFingerprint.Compute(once), ComponentFingerprint.Compute(twice));
    }

    [Fact]
    public void Empty_input_yields_unknown()
    {
        Assert.Equal("", ComponentFingerprint.Compute(new List<ComponentFile>()));
        Assert.Equal("", ComponentFingerprint.Compute((IEnumerable<ComponentFile>)null!));
        Assert.Equal("", ComponentFingerprint.Compute((ComponentManifest)null!));
    }

    [Fact]
    public void Differs_never_reports_a_difference_when_a_side_is_unknown()
    {
        Assert.False(ComponentFingerprint.Differs(null, "abc"));
        Assert.False(ComponentFingerprint.Differs("abc", null));
        Assert.False(ComponentFingerprint.Differs("", "abc"));
        Assert.False(ComponentFingerprint.Differs("abc", "abc"));
        Assert.True(ComponentFingerprint.Differs("abc", "xyz"));
    }

    [Fact]
    public void Manifest_overload_matches_the_file_list_overload()
    {
        var files = new[] { File("modA", "content/a.txt", "shaA") };
        var manifest = new ComponentManifest("modA", TargetDomain.Mods, "root", files);

        Assert.Equal(ComponentFingerprint.Compute(files), ComponentFingerprint.Compute(manifest));
    }
}
