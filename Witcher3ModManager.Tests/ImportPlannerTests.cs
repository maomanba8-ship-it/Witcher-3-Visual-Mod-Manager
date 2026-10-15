using System;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Import;
using Xunit;

namespace Witcher3ModManager.Tests;

public class ImportPlannerTests
{
    [Fact]
    public void Fresh_components_are_planned_as_new_without_a_confirmation()
    {
        var analysis = Analysis(
            Component("modX", ComponentKind.Mod),
            Component("dlcY", ComponentKind.Dlc));

        var plan = ImportPlanner.Build(analysis);

        Assert.False(plan.IsRejected);
        Assert.False(plan.RequiresConfirmation);
        Assert.Equal(new[] { ImportAction.Create, ImportAction.Create }, plan.Components.Select(c => c.Action).ToArray());
        Assert.Empty(plan.Overwrites);
    }

    [Fact]
    public void An_existing_target_is_planned_as_an_overwrite_and_asks_for_confirmation()
    {
        var analysis = Analysis(Component("modX", ComponentKind.Mod));

        var plan = ImportPlanner.Build(analysis, new[] { "modX" });

        var component = Assert.Single(plan.Components);
        Assert.Equal(ImportAction.Update, component.Action);
        Assert.True(plan.RequiresConfirmation);
        Assert.Equal("modX", Assert.Single(plan.Overwrites).Name);
    }

    [Fact]
    public void Existing_names_are_matched_ignoring_case()
    {
        var analysis = Analysis(Component("ModX", ComponentKind.Mod));

        var plan = ImportPlanner.Build(analysis, new[] { "modx" });

        Assert.Equal(ImportAction.Update, Assert.Single(plan.Components).Action);
    }

    [Fact]
    public void The_supplied_naming_rule_decides_what_counts_as_existing()
    {
        // The deployment adds a "mod" prefix for a bare folder, so a package folder named Foo
        // collides with an installed modFoo. The plan has to use that same rule.
        var analysis = Analysis(Component("Foo", ComponentKind.Mod));

        var naive = ImportPlanner.Build(analysis, new[] { "modFoo" });
        var aware = ImportPlanner.Build(analysis, new[] { "modFoo" }, c => "mod" + c.Name);

        Assert.Equal(ImportAction.Create, Assert.Single(naive.Components).Action);
        Assert.Equal(ImportAction.Update, Assert.Single(aware.Components).Action);
        Assert.Equal("modFoo", Assert.Single(aware.Components).DeployName);
    }

    [Fact]
    public void An_unsupported_component_needs_a_decision_instead_of_a_target()
    {
        var analysis = Analysis(Component("mystery", ComponentKind.Unsupported));

        var plan = ImportPlanner.Build(analysis);

        Assert.Equal(ImportAction.Unsupported, Assert.Single(plan.Components).Action);
        Assert.True(plan.RequiresConfirmation);
    }

    [Fact]
    public void A_component_without_a_usable_name_is_unsupported()
    {
        var analysis = Analysis(Component("   ", ComponentKind.Mod));

        var plan = ImportPlanner.Build(analysis);

        Assert.Equal(ImportAction.Unsupported, Assert.Single(plan.Components).Action);
        Assert.True(plan.RequiresConfirmation);
    }

    [Fact]
    public void Analyzer_decisions_force_a_confirmation_even_when_nothing_is_overwritten()
    {
        var analysis = new AnalysisResult(
            new[] { Component("Foo", ComponentKind.Mod) },
            Array.Empty<string>(),
            new[] { "confirm target for component 'Foo'" },
            null);

        var plan = ImportPlanner.Build(analysis);

        Assert.True(plan.RequiresConfirmation);
        Assert.Equal(ImportAction.Create, Assert.Single(plan.Components).Action);
        Assert.Single(plan.Decisions);
    }

    [Fact]
    public void A_rejected_analysis_yields_a_rejected_plan_with_nothing_to_write()
    {
        var analysis = new AnalysisResult(
            Array.Empty<PackageComponent>(),
            new[] { "no recognizable mod/dlc/bin component found" },
            Array.Empty<string>(),
            "unknown_structure");

        var plan = ImportPlanner.Build(analysis);

        Assert.True(plan.IsRejected);
        Assert.Equal("unknown_structure", plan.RejectReason);
        Assert.True(plan.IsEmpty);
        Assert.False(plan.RequiresConfirmation);
        Assert.Single(plan.Warnings);
    }

    [Fact]
    public void A_missing_analysis_is_rejected_rather_than_treated_as_empty()
    {
        var plan = ImportPlanner.Build(null);

        Assert.True(plan.IsRejected);
        Assert.Equal(ImportPlanner.AnalysisMissingReason, plan.RejectReason);
        Assert.True(plan.IsEmpty);
    }

    private static PackageComponent Component(string name, ComponentKind kind) =>
        new(name, kind, "/tmp/" + name, new[] { "test" });

    private static AnalysisResult Analysis(params PackageComponent[] components) =>
        new(components, Array.Empty<string>(), Array.Empty<string>(), null);
}
