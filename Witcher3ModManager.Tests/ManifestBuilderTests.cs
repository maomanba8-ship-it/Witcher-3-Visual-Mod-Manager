using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Manifest;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ManifestBuilderTests
{
    [Fact]
    public void Build_records_every_file_with_normalized_path_length_and_hash()
    {
        var root = CreateTempComponentRoot(
            ("content/a.txt", "AAA"),
            ("scripts/x.ws", "SCRIPT"),
            ("metadata.json", "{}"));

        try
        {
            var manifest = ManifestBuilder.Build(root, "modFoo", TargetDomain.Mods);

            Assert.Equal("modFoo", manifest.ComponentIdentity);
            Assert.Equal(TargetDomain.Mods, manifest.Domain);
            Assert.Equal(3, manifest.FileCount);

            var aTxt = manifest.Files.Single(f => f.NormalizedRelativePath == "content/a.txt");
            Assert.Equal(3, aTxt.Length);
            Assert.Equal(Sha256Of("AAA"), aTxt.Sha256);

            var xWs = manifest.Files.Single(f => f.NormalizedRelativePath == "scripts/x.ws");
            Assert.Equal(6, xWs.Length);
            Assert.Equal(Sha256Of("SCRIPT"), xWs.Sha256);

            Assert.Equal("metadata.json", manifest.Files.Single(f => f.NormalizedRelativePath == "metadata.json").NormalizedRelativePath);
            Assert.Equal(manifest.Files.Count, manifest.Files.Select(f => f.NormalizedRelativePath).Distinct().Count());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_feeding_same_manifest_twice_is_not_a_conflict()
    {
        var root = CreateTempComponentRoot(("content/a.txt", "AAA"));

        try
        {
            var manifest = ManifestBuilder.Build(root, "modFoo", TargetDomain.Mods);
            var dup = manifest.Files.Concat(manifest.Files).ToArray();
            Assert.Empty(ConflictAnalyzer.Analyze(dup));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_throws_on_missing_root()
    {
        var missing = Path.Combine(Path.GetTempPath(), "w3mm_missing_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.Throws<DirectoryNotFoundException>(() => ManifestBuilder.Build(missing, "modX", TargetDomain.Mods));
    }

    private static string CreateTempComponentRoot(params (string Rel, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "w3mm_manifest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }

    private static string Sha256Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}