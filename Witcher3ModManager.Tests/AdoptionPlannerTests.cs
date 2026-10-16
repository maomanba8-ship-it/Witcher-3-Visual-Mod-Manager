using System;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class AdoptionPlannerTests
{
    [Fact]
    public void A_folder_becomes_a_component_with_what_is_in_it_now()
    {
        var plan = Build(new AdoptionComponent(TargetDomain.Mods, "modBetterCombat", "fingerprint"));

        var component = Assert.Single(plan.Components);
        Assert.Equal(TargetDomain.Mods, component.Domain);
        Assert.Equal("modBetterCombat", component.StorageFolder);

        var target = Assert.Single(component.Targets);
        Assert.Equal("mods/modBetterCombat", target.RelativePath);
        Assert.Equal(TargetKind.Folder, target.Kind);
        Assert.Equal("fingerprint", target.WrittenHash);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void A_dlc_folder_is_recorded_in_the_dlc_domain()
    {
        var plan = Build(new AdoptionComponent(TargetDomain.Dlc, "dlcBetterCombat", "fingerprint"));

        var target = Assert.Single(Assert.Single(plan.Components).Targets);
        Assert.Equal("dlc/dlcBetterCombat", target.RelativePath);
    }

    [Fact]
    public void Bin_files_inside_an_adopted_folder_are_recorded_as_one_component()
    {
        var plan = Build(new AdoptionComponent(TargetDomain.Mods, "modA", "fingerprint", new[]
        {
            new AdoptedFile(@"config\r4game\input.xml", "hashInput"),
            new AdoptedFile("config/other.xml", "hashOther")
        }));

        var bin = Assert.Single(plan.Components, c => c.Domain == TargetDomain.Bin);
        Assert.Equal(2, bin.Targets.Count);
        // Paths are normalised the way the index spells them, whichever separator came in.
        Assert.Contains(bin.Targets, t => t.RelativePath == "bin/config/r4game/input.xml" && t.Kind == TargetKind.File);
        Assert.Contains(bin.Targets, t => t.RelativePath == "bin/config/other.xml" && t.WrittenHash == "hashOther");
    }

    [Fact]
    public void A_folder_someone_else_owns_is_refused_and_named()
    {
        var index = LibraryIndexUpdater.WithPackage(LibraryIndex.Empty, "other", "Better Combat", new[]
        {
            new ComponentRecord("other:Mods:modbettercombat", TargetDomain.Mods, "modBetterCombat",
                new[] { new TargetRecord("mods/modBetterCombat", TargetKind.Folder, "hash", DateTime.UnixEpoch) })
        });

        var plan = AdoptionPlanner.Build("pkg", "Mod Better Combat", 
            new[] { new AdoptionComponent(TargetDomain.Mods, "modBetterCombat", "fingerprint") }, index);

        Assert.Empty(plan.Components);
        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(AdoptionProblem.AlreadyOwned, warning.Problem);
        Assert.Equal("mods/modBetterCombat", warning.Subject);
        Assert.Equal("Better Combat", warning.Owner);
    }

    [Fact]
    public void A_bin_file_someone_else_owns_is_refused_on_its_own()
    {
        var index = LibraryIndexUpdater.WithPackage(LibraryIndex.Empty, "other", "Input Tweaks", new[]
        {
            new ComponentRecord("other:Bin:", TargetDomain.Bin, "",
                new[] { new TargetRecord("bin/config/input.xml", TargetKind.File, "hash", DateTime.UnixEpoch) })
        });

        var plan = AdoptionPlanner.Build("pkg", "Mod A", new[]
        {
            new AdoptionComponent(TargetDomain.Mods, "modA", "fingerprint", new[]
            {
                new AdoptedFile("config/input.xml", "hashInput"),
                new AdoptedFile("config/other.xml", "hashOther")
            })
        }, index);

        // The folder and the free bin file are adopted; only the claimed one is left out.
        Assert.Contains(plan.Components, c => c.Domain == TargetDomain.Mods);
        var bin = Assert.Single(plan.Components, c => c.Domain == TargetDomain.Bin);
        Assert.Equal("bin/config/other.xml", Assert.Single(bin.Targets).RelativePath);
        Assert.Equal("Input Tweaks", Assert.Single(plan.Warnings).Owner);
    }

    [Fact]
    public void A_folder_that_could_not_be_fingerprinted_is_refused()
    {
        var plan = Build(new AdoptionComponent(TargetDomain.Mods, "modA", ""));

        Assert.Empty(plan.Components);
        Assert.Equal(AdoptionProblem.Unreadable, Assert.Single(plan.Warnings).Problem);
    }

    [Fact]
    public void A_paired_mod_and_dlc_become_two_components_of_one_package()
    {
        var plan = AdoptionPlanner.Build("pkg", "Mod A", new[]
        {
            new AdoptionComponent(TargetDomain.Mods, "modA", "fingerprintA"),
            new AdoptionComponent(TargetDomain.Dlc, "dlcA", "fingerprintB")
        }, LibraryIndex.Empty);

        Assert.Equal(2, plan.Components.Count);
        Assert.All(plan.Components, c => Assert.StartsWith("pkg:", c.ComponentId, StringComparison.Ordinal));
    }

    [Fact]
    public void Blank_folder_names_are_ignored()
    {
        var plan = AdoptionPlanner.Build("pkg", "Mod A", new[]
        {
            new AdoptionComponent(TargetDomain.Mods, "", "fingerprint"),
            new AdoptionComponent(TargetDomain.Mods, "   ", "fingerprint")
        }, LibraryIndex.Empty);

        Assert.Empty(plan.Components);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Nothing_found_yields_an_empty_plan()
    {
        Assert.Empty(AdoptionPlanner.Build("pkg", "Mod A", null, LibraryIndex.Empty).Components);
        Assert.Empty(AdoptionPlanner.Build("pkg", "Mod A", Array.Empty<AdoptionComponent>(), null).Components);
    }

    private static AdoptionPlan Build(params AdoptionComponent[] components) =>
        AdoptionPlanner.Build("pkg", "Mod A", components, LibraryIndex.Empty);
}
