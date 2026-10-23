using System.Text.RegularExpressions;

namespace Witcher3ModManager.Core.Settings;

/// <summary>
/// One entry of <c>Documents\The Witcher 3\mods.settings</c>.
/// </summary>
public sealed record ModsSettingsEntry(string ModName, int Priority, bool Enabled);

/// <summary>
/// Reads and rewrites the game's mods.settings file in the format the game itself understands:
///
/// <code>
/// [modName]
/// Enabled=1
/// Priority=3
/// </code>
///
/// The older <c>"modName" = 3</c> rows written by earlier builds of this manager are still
/// understood when reading, and are converted to sections when rewriting, so an existing file
/// migrates itself on the next save. Rows that do not belong to this manager are never dropped:
/// they are kept byte-for-byte (sections) or converted losslessly (legacy rows).
/// </summary>
public static partial class ModsSettingsFile
{
    public const string EnabledKey = "Enabled";
    public const string PriorityKey = "Priority";

    [GeneratedRegex("^\\s*\"([^\"]+)\"\\s*=\\s*(\\d+)\\s*$")]
    private static partial Regex LegacyRow();

    /// <summary>
    /// Parses sections and legacy rows. A section without a priority is ignored (the game cannot
    /// use it either); a section without an explicit Enabled value counts as enabled, which is
    /// how most mods.settings files in the wild are written.
    /// </summary>
    public static List<ModsSettingsEntry> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var entries = new List<ModsSettingsEntry>();
        string? section = null;
        bool? enabled = null;
        int? priority = null;

        void Flush()
        {
            if (section is { Length: > 0 } && priority.HasValue)
                entries.Add(new ModsSettingsEntry(section, priority.Value, enabled ?? true));

            section = null;
            enabled = null;
            priority = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (IsSectionHeader(line))
            {
                Flush();
                section = line[1..^1].Trim();
                continue;
            }

            var legacy = LegacyRow().Match(line);
            if (legacy.Success)
            {
                Flush();
                if (int.TryParse(legacy.Groups[2].Value, out var legacyPriority))
                    entries.Add(new ModsSettingsEntry(legacy.Groups[1].Value, legacyPriority, true));
                continue;
            }

            if (section == null) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Equals(EnabledKey, StringComparison.OrdinalIgnoreCase))
                enabled = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals(PriorityKey, StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(value, out var parsed))
                priority = parsed;
            // Any other key (VK today) is not ours to interpret; it dies with its section.
        }

        Flush();
        return entries;
    }

    /// <summary>
    /// Produces the new file contents. Sections and legacy rows whose name is in
    /// <paramref name="managedNames"/> are removed and re-emitted from <paramref name="desired"/>;
    /// everything else is preserved in place.
    /// </summary>
    public static List<string> Rewrite(
        IEnumerable<string> existingLines,
        IEnumerable<(string ModName, int Priority)> desired,
        ISet<string> managedNames)
    {
        ArgumentNullException.ThrowIfNull(existingLines);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(managedNames);

        var managed = new HashSet<string>(managedNames, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var skippingManagedSection = false;

        foreach (var raw in existingLines)
        {
            var line = raw.Trim();

            if (IsSectionHeader(line))
            {
                var name = line[1..^1].Trim();
                skippingManagedSection = managed.Contains(name);
                if (!skippingManagedSection) result.Add(raw);
                continue;
            }

            if (skippingManagedSection)
            {
                // The block belongs to a mod this manager owns, so its settings go away with it —
                // but a comment is something a person wrote, and it is kept.
                if (line.StartsWith(';') || line.StartsWith('#'))
                    result.Add(raw);
                continue;
            }

            var legacy = LegacyRow().Match(line);
            if (legacy.Success)
            {
                var name = legacy.Groups[1].Value;
                if (!managed.Contains(name) && int.TryParse(legacy.Groups[2].Value, out var priority))
                    AppendSection(result, name, priority);
                continue;
            }

            result.Add(raw);
        }

        foreach (var (name, priority) in Normalize(desired))
            AppendSection(result, name, priority);

        return result;
    }

    private static IEnumerable<(string ModName, int Priority)> Normalize(
        IEnumerable<(string ModName, int Priority)> desired)
    {
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, priority) in desired)
        {
            var trimmed = name?.Trim() ?? "";
            if (trimmed.Length == 0) continue;
            byName[trimmed] = priority;
        }

        return byName
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key, pair.Value));
    }

    private static void AppendSection(List<string> lines, string modName, int priority)
    {
        if (lines.Count > 0 && lines[^1].Trim().Length != 0)
            lines.Add("");

        lines.Add($"[{modName}]");
        lines.Add("Enabled=1");
        lines.Add($"{PriorityKey}={priority}");
    }

    private static bool IsSectionHeader(string line) =>
        line.Length >= 2 && line[0] == '[' && line[^1] == ']';
}
