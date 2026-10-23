using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Settings;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ModsSettingsFileTests
{
    [Fact]
    public void Sections_are_read_with_enabled_state_and_priority()
    {
        var entries = ModsSettingsFile.Parse(new[]
        {
            "[modA]",
            "Enabled=1",
            "Priority=2",
            "",
            "[modB]",
            "Enabled=0",
            "Priority=7"
        });

        Assert.Equal(2, entries.Count);
        Assert.Equal(new ModsSettingsEntry("modA", 2, true), entries[0]);
        Assert.Equal(new ModsSettingsEntry("modB", 7, false), entries[1]);
    }

    [Fact]
    public void Legacy_rows_are_read_as_enabled()
    {
        var entries = ModsSettingsFile.Parse(new[]
        {
            "\"modA\" = 4",
            "\"modB\" = 5"
        });

        Assert.Equal(new[] { ("modA", 4), ("modB", 5) },
            entries.Select(e => (e.ModName, e.Priority)).ToArray());
        Assert.All(entries, e => Assert.True(e.Enabled));
    }

    [Fact]
    public void Section_without_explicit_enabled_counts_as_enabled()
    {
        var entry = Assert.Single(ModsSettingsFile.Parse(new[] { "[modA]", "Priority=3" }));

        Assert.True(entry.Enabled);
        Assert.Equal(3, entry.Priority);
    }

    [Fact]
    public void Rewrite_replaces_managed_section_and_keeps_foreign_section_verbatim()
    {
        var existing = new[]
        {
            "[modForeign]",
            "Enabled=1",
            "Priority=9",
            "",
            "[modA]",
            "Enabled=0",
            "Priority=1"
        };

        var rewritten = ModsSettingsFile.Rewrite(
            existing,
            new[] { ("modA", 5) },
            new HashSet<string>(new[] { "modA" }, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new[]
        {
            "[modForeign]",
            "Enabled=1",
            "Priority=9",
            "",
            "[modA]",
            "Enabled=1",
            "Priority=5"
        }, rewritten);
    }

    [Fact]
    public void Rewrite_removes_stale_managed_section()
    {
        var existing = new[]
        {
            "[modGone]",
            "Enabled=1",
            "Priority=2",
            "",
            "[modKeep]",
            "Enabled=1",
            "Priority=3"
        };

        var rewritten = ModsSettingsFile.Rewrite(
            existing,
            Array.Empty<(string, int)>(),
            new HashSet<string>(new[] { "modGone" }, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new[] { "[modKeep]", "Enabled=1", "Priority=3" }, rewritten);
    }

    [Fact]
    public void Rewrite_converts_foreign_legacy_row_instead_of_dropping_it()
    {
        var rewritten = ModsSettingsFile.Rewrite(
            new[] { "\"modForeign\" = 8" },
            Array.Empty<(string, int)>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new[] { "[modForeign]", "Enabled=1", "Priority=8" }, rewritten);
    }

    [Fact]
    public void Rewrite_writes_sections_sorted_by_priority()
    {
        var rewritten = ModsSettingsFile.Rewrite(
            Array.Empty<string>(),
            new[] { ("modLate", 9), ("modEarly", 1) },
            new HashSet<string>(new[] { "modLate", "modEarly" }, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new[]
        {
            "[modEarly]", "Enabled=1", "Priority=1",
            "",
            "[modLate]", "Enabled=1", "Priority=9"
        }, rewritten);
    }

    [Fact]
    public void Rewritten_file_contains_no_legacy_rows_so_script_merger_can_parse_it()
    {
        var existing = new[]
        {
            "; comment kept",
            "\"modOld\" = 3"
        };

        var rewritten = ModsSettingsFile.Rewrite(
            existing,
            new[] { ("modNew", 1) },
            new HashSet<string>(new[] { "modOld", "modNew" }, StringComparer.OrdinalIgnoreCase));

        Assert.DoesNotContain(rewritten, line => line.TrimStart().StartsWith('"'));
        Assert.Equal("; comment kept", rewritten[0]);
    }

    [Fact]
    public void Round_trip_keeps_priorities()
    {
        var rewritten = ModsSettingsFile.Rewrite(
            new[] { "[modA]", "Enabled=1", "Priority=2" },
            new[] { ("modA", 6), ("modB", 7) },
            new HashSet<string>(new[] { "modA", "modB" }, StringComparer.OrdinalIgnoreCase));

        var entries = ModsSettingsFile.Parse(rewritten);

        Assert.Equal(new[] { ("modA", 6), ("modB", 7) },
            entries.Select(e => (e.ModName, e.Priority)).ToArray());
    }
}
