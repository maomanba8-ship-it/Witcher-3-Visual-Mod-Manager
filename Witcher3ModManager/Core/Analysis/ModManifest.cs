using System.IO;
using System.Text.Json;

namespace Witcher3ModManager.Core.Analysis;

/// <summary>
/// What a v5.0 mod's own manifest says about itself. The file is <c>content/info.json</c>; it
/// carries the display metadata and, unlike anything in 4.x, a list of dependencies.
/// </summary>
public sealed record ModManifestInfo(
    string? Name,
    string? Author,
    string? Version,
    string? Description,
    int? GameVersion,
    IReadOnlyList<string> Dependencies);

/// <summary>
/// Reads the 5.0 mod manifest (<c>content/info.json</c>).
///
/// This is metadata, not structure: a package is recognised by its folders, and a missing or
/// unreadable manifest is a normal package without metadata, never a broken one. Nothing here
/// decides where a mod goes.
///
/// The manifest's own <c>name</c> is an id-shaped string ("gwentfaithfullyeroticartwork"), which
/// makes a poor display name, so the importer only takes the fields a folder cannot provide:
/// author, version, description.
/// </summary>
public static class ModManifest
{
    public const string RelativePath = "content/info.json";

    public static ModManifestInfo? Read(string? componentRoot)
    {
        if (string.IsNullOrWhiteSpace(componentRoot)) return null;

        var path = Path.Combine(componentRoot, "content", "info.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            return new ModManifestInfo(
                Text(root, "name"),
                Text(root, "author"),
                Text(root, "version"),
                Text(root, "description"),
                Number(root, "gameVersion"),
                Strings(root, "dependencies"));
        }
        catch
        {
            // A damaged manifest costs metadata, nothing else.
            return null;
        }
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString())
            : null;

    /// <summary>
    /// gameVersion is written both ways in the wild: as a number (29) and as a string ("29"). Both
    /// are the same fact, so both are read; anything else is simply unknown.
    /// </summary>
    private static int? Number(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value)) return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    private static IReadOnlyList<string> Strings(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                items.Add(item.GetString()!);
        }
        return items;
    }
}
