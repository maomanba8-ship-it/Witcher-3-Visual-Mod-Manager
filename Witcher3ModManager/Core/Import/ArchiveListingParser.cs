using System.Globalization;

namespace Witcher3ModManager.Core.Import;

/// <summary>
/// Parses the machine-readable listing that 7-Zip prints for "-slt". Keeping the parse separate
/// from running the tool means the safety rules can be exercised against hand-written listings,
/// and a change in the tool's output format has exactly one place to be dealt with.
///
/// The listing starts with a header block that describes the archive itself; that block is
/// recognised by its "Type" key and never reported as content.
/// </summary>
public static class ArchiveListingParser
{
    public static IReadOnlyList<ArchiveEntryInfo> Parse(string? listingOutput)
    {
        if (string.IsNullOrWhiteSpace(listingOutput)) return Array.Empty<ArchiveEntryInfo>();

        var entries = new List<ArchiveEntryInfo>();
        string? path = null;
        long size = 0;
        var isDirectory = false;
        var isArchiveHeader = false;

        void Flush()
        {
            if (path is not null && !isArchiveHeader)
                entries.Add(new ArchiveEntryInfo(path, isDirectory ? 0 : size, isDirectory));

            path = null;
            size = 0;
            isDirectory = false;
            isArchiveHeader = false;
        }

        foreach (var raw in listingOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal))
            {
                Flush();
                continue;
            }

            var separator = line.IndexOf(" = ", StringComparison.Ordinal);
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 3)..];

            switch (key)
            {
                case "Path":
                    path = value;
                    break;
                case "Type":
                    isArchiveHeader = true;
                    break;
                case "Folder":
                    isDirectory = value.Trim() == "+";
                    break;
                case "Size":
                    size = long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0;
                    break;
            }
        }

        Flush();
        return entries;
    }
}
