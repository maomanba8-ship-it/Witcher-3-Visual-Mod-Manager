using System;
using System.Collections.Generic;
using System.Linq;
using Witcher3ModManager.Core.Migration;
using Xunit;

namespace Witcher3ModManager.Tests;

public class LegacyLibraryAuditorTests
{
    [Fact]
    public void A_healthy_library_produces_no_findings()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[] { Entry("modA", mods: new[] { "modA" }), Entry("dlcB", dlcs: new[] { "dlcB" }) },
            storageFolderExists: _ => true,
            gameFolderExists: (_, _) => true);

        Assert.Equal(2, audit.EntryCount);
        Assert.Empty(audit.Findings);
        Assert.True(audit.IsClean);
    }

    [Fact]
    public void A_record_with_no_component_at_all_is_unattributable()
    {
        var audit = LegacyLibraryAuditor.Audit(new[] { Entry("ghost") });

        var finding = Assert.Single(audit.Findings);
        Assert.Equal(LegacyAuditKind.Unattributable, finding.Kind);
        Assert.Equal(new[] { "ghost" }, finding.Entries);
    }

    [Fact]
    public void Bin_only_records_are_flagged_but_still_migratable()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[] { Entry("modBin", bin: new[] { "config/a.xml" }) },
            storageFolderExists: _ => true);

        var finding = Assert.Single(audit.Findings);
        Assert.Equal(LegacyAuditKind.BinOnly, finding.Kind);
    }

    [Fact]
    public void A_folder_missing_from_storage_is_reported_with_its_name()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[]
            {
                Entry("modA", mods: new[] { "modA" }),
                Entry("modGone", mods: new[] { "modGone" })
            },
            storageFolderExists: folder => folder != "modGone");

        var finding = Assert.Single(audit.OfKind(LegacyAuditKind.StorageFolderMissing));
        Assert.Equal(new[] { "modGone" }, finding.Entries);
        Assert.Equal(new[] { "modGone" }, finding.Subjects);
    }

    [Fact]
    public void The_same_folder_listed_twice_in_one_record_is_reported()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[] { Entry("modA", mods: new[] { "modA", "MODA" }) });

        var finding = Assert.Single(audit.OfKind(LegacyAuditKind.DuplicateTargetFolder));
        Assert.Equal(new[] { "modA" }, finding.Entries);
    }

    [Fact]
    public void A_folder_claimed_by_two_records_is_shared_and_not_an_error()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[]
            {
                Entry("modA", mods: new[] { "modShared", "modA" }),
                Entry("modB", mods: new[] { "modShared", "modB" })
            },
            storageFolderExists: _ => true,
            gameFolderExists: (_, _) => true);

        var finding = Assert.Single(audit.OfKind(LegacyAuditKind.SharedFolder));
        Assert.Equal(new[] { "modA", "modB" }, finding.Entries);
        Assert.Equal(new[] { "modShared" }, finding.Subjects);
        Assert.True(audit.IsClean);   // shared folders alone are not a problem
    }

    [Fact]
    public void An_enabled_record_absent_from_the_game_directory_is_reported()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[]
            {
                Entry("modHere", mods: new[] { "modHere" }, enabled: true),
                Entry("modMissing", mods: new[] { "modMissing" }, enabled: true),
                Entry("modOff", mods: new[] { "modOff" }, enabled: false)
            },
            storageFolderExists: _ => true,
            gameFolderExists: (folder, _) => folder == "modHere");

        var finding = Assert.Single(audit.OfKind(LegacyAuditKind.EnabledButMissingInGame));
        Assert.Equal(new[] { "modMissing" }, finding.Entries);
    }

    [Fact]
    public void The_game_check_distinguishes_mods_from_dlc()
    {
        var seen = new List<(string Folder, bool IsDlc)>();
        LegacyLibraryAuditor.Audit(
            new[] { Entry("modA", mods: new[] { "modA" }, dlcs: new[] { "dlcB" }, enabled: true) },
            // The mod folder is absent, so the dlc folder is asked about next; the probe stops
            // once an entry is served, which is all the verdict needs.
            gameFolderExists: (folder, isDlc) => { seen.Add((folder, isDlc)); return folder == "dlcB"; });

        Assert.Contains(("modA", false), seen);
        Assert.Contains(("dlcB", true), seen);
    }

    [Fact]
    public void Without_predicates_only_record_level_checks_run()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[] { Entry("modA", mods: new[] { "modA" }, enabled: true) });

        // No claim about the file system was made, so none is invented.
        Assert.Empty(audit.Findings);
    }

    [Fact]
    public void Blank_folder_names_are_ignored()
    {
        var audit = LegacyLibraryAuditor.Audit(
            new[] { Entry("modA", mods: new[] { "", "   ", "modA" }) },
            storageFolderExists: _ => true,
            gameFolderExists: (_, _) => true);

        Assert.Empty(audit.Findings);
    }

    [Fact]
    public void A_missing_library_is_an_empty_audit()
    {
        var audit = LegacyLibraryAuditor.Audit(null);

        Assert.Equal(0, audit.EntryCount);
        Assert.Empty(audit.Findings);
    }

    private static LegacyLibraryEntry Entry(
        string name,
        IReadOnlyList<string>? mods = null,
        IReadOnlyList<string>? dlcs = null,
        IReadOnlyList<string>? bin = null,
        bool enabled = false) =>
        new(name, name, enabled,
            mods ?? Array.Empty<string>(),
            dlcs ?? Array.Empty<string>(),
            bin ?? Array.Empty<string>());
}
