using System.IO;
using System.Xml.Linq;

namespace Witcher3ModManager.Core.Analysis.Merge;

/// <summary>
/// One file Script Merger has merged: the paths it applies to, the mods that went into it, and
/// the mod that now carries the merged result.
/// </summary>
public sealed record MergeEntry(
    string RelativePath,
    IReadOnlyList<string> IncludedMods,
    string MergedModName)
{
    /// <summary>
    /// The path as the conflict analysis records it. Script Merger stores paths relative to the
    /// game's script root ("game\player\r4Player.ws"); our conflicts use the full mod path.
    /// </summary>
    public string ConflictPath
    {
        get
        {
            var path = RelativePath.Replace('\\', '/').TrimStart('/');
            if (!path.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                path = "content/scripts/" + path;
            return path.ToLowerInvariant();
        }
    }
}

/// <summary>
/// Reads Script Merger Remastered's <c>MergeInventory.xml</c>. The file lives next to
/// <c>WitcherScriptMerger.exe</c>, not in the game directory, and lists every merge it created.
///
/// Pure parsing: a missing or malformed file yields no entries. Treating "no inventory" as "no
/// merges" is the conservative direction — the conflicts stay visible as red.
/// </summary>
public static class MergeInventoryReader
{
    public const string FileName = "MergeInventory.xml";

    public static IReadOnlyList<MergeEntry> Read(string xmlPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                return Array.Empty<MergeEntry>();

            var document = XDocument.Load(xmlPath);
            var merges = document.Root?.Elements("Merge");
            if (merges is null) return Array.Empty<MergeEntry>();

            var entries = new List<MergeEntry>();
            foreach (var merge in merges)
            {
                var relativePath = ((string?)merge.Element("RelativePath"))?.Trim();
                var mergedMod = ((string?)merge.Element("MergedModName"))?.Trim();
                if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(mergedMod))
                    continue;

                var included = merge.Elements("IncludedMod")
                    .Select(element => (element.Value ?? "").Trim())
                    .Where(name => name.Length > 0)
                    .ToList();

                entries.Add(new MergeEntry(relativePath, included, mergedMod));
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return Array.Empty<MergeEntry>();
        }
    }

    /// <summary>Reads the inventory that belongs to a given Script Merger executable.</summary>
    public static IReadOnlyList<MergeEntry> ReadFromScriptMerger(string? scriptMergerExePath)
    {
        if (string.IsNullOrWhiteSpace(scriptMergerExePath)) return Array.Empty<MergeEntry>();

        var directory = Path.GetDirectoryName(scriptMergerExePath);
        if (string.IsNullOrWhiteSpace(directory)) return Array.Empty<MergeEntry>();

        return Read(Path.Combine(directory, FileName));
    }
}
