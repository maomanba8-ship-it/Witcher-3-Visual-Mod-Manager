using System.IO;
using System.Security.Cryptography;
using System.Text;
using Witcher3ModManager.Core.Analysis.Rsblob;

namespace Witcher3ModManager.Core.Analysis.Script;

/// <summary>What a component's script files contained, and whether the blob could be read.</summary>
public sealed record ScriptScanResult(
    IReadOnlyList<ScriptSymbol> Symbols,
    bool HasRsblob,
    string? RsblobError,
    string? Fingerprint = null);

/// <summary>
/// Collects the script symbols a component contributes: the compiled blob (if any) plus every
/// loose <c>.ws</c> file under <c>content\scripts</c>. Read-only; a file that cannot be read is
/// reported as unknown rather than as a finding.
/// </summary>
public static class ScriptSymbolScanner
{
    public static ScriptScanResult ScanComponent(string componentRoot)
    {
        if (string.IsNullOrWhiteSpace(componentRoot) || !Directory.Exists(componentRoot))
            return new ScriptScanResult(Array.Empty<ScriptSymbol>(), false, null);

        var symbols = new List<ScriptSymbol>();
        var hasRsblob = false;
        string? rsblobError = null;
        var files = new List<(string Path, string Hash)>();

        var blobPath = Path.Combine(componentRoot, "content", "precompiled.rsblob");
        if (File.Exists(blobPath))
        {
            hasRsblob = true;
            try
            {
                var bytes = File.ReadAllBytes(blobPath);
                symbols.AddRange(RsblobReader.Read(bytes).Symbols);
                files.Add(("content/precompiled.rsblob", Hash(bytes)));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Unknown format or unreadable file: the caller keeps the "cannot tell" verdict.
                rsblobError = ex.Message;
            }
        }

        var scriptsRoot = Path.Combine(componentRoot, "content", "scripts");
        if (Directory.Exists(scriptsRoot))
        {
            foreach (var file in EnumerateScripts(scriptsRoot))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    symbols.AddRange(ScriptAnnotationReader.Parse(text));
                    files.Add((Path.GetRelativePath(componentRoot, file).Replace('\\', '/'), Hash(Encoding.UTF8.GetBytes(text))));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // One unreadable script must not abort the whole scan.
                }
            }
        }

        return new ScriptScanResult(symbols, hasRsblob, rsblobError, ComputeFingerprint(files));
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static string? ComputeFingerprint(List<(string Path, string Hash)> files)
    {
        if (files.Count == 0) return null;

        var builder = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            builder.Append(file.Path.ToLowerInvariant()).Append('\0').Append(file.Hash).Append('\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static IEnumerable<string> EnumerateScripts(string root)
    {
        try
        {
            return Directory.GetFiles(root, "*.ws", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
