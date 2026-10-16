using System;
using System.Collections.Generic;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class PresetSelectionTests
{
    private static readonly string[] Folders = { "modA", "dlcA" };

    [Fact]
    public void A_new_preset_is_read_by_package_id()
    {
        var states = States(("pkg1", true));

        Assert.True(PresetSelection.Wanted("pkg1", Folders, states, PresetSelection.CurrentVersion));
    }

    [Fact]
    public void A_new_preset_says_nothing_about_other_packages()
    {
        var states = States(("pkg1", true));

        Assert.Null(PresetSelection.Wanted("pkg2", Folders, states, PresetSelection.CurrentVersion));
    }

    [Fact]
    public void An_old_preset_is_read_by_folder_name()
    {
        // Files written before the change carry no version and name component folders.
        var states = States(("modA", true));

        Assert.True(PresetSelection.Wanted("pkg1", Folders, states, version: 0));
    }

    [Fact]
    public void An_old_preset_matches_on_any_of_the_packages_folders()
    {
        var states = States(("dlcA", false));

        Assert.False(PresetSelection.Wanted("pkg1", Folders, states, version: 0));
    }

    [Fact]
    public void An_old_preset_that_does_not_name_the_package_says_nothing()
    {
        var states = States(("modB", true));

        Assert.Null(PresetSelection.Wanted("pkg1", Folders, states, version: 0));
    }

    [Fact]
    public void A_preset_without_a_package_id_falls_back_to_folders()
    {
        var states = States(("modA", true));

        Assert.True(PresetSelection.Wanted("", Folders, states, PresetSelection.CurrentVersion));
        Assert.True(PresetSelection.Wanted(null, Folders, states, PresetSelection.CurrentVersion));
    }

    [Fact]
    public void An_empty_preset_says_nothing_about_anything()
    {
        Assert.Null(PresetSelection.Wanted("pkg1", Folders, States(), PresetSelection.CurrentVersion));
        Assert.Null(PresetSelection.Wanted("pkg1", Folders, null, PresetSelection.CurrentVersion));
        Assert.Null(PresetSelection.Wanted("pkg1", null, null, version: 0));
    }

    private static Dictionary<string, bool> States(params (string Key, bool Value)[] entries)
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries) states[key] = value;
        return states;
    }
}
