using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Import;

/// <summary>
/// Turns a read-only structure analysis into an import plan. Pure: no file system access, no
/// storage, no UI. The caller decides which target names already exist and how a component's
/// folder is named on disk, so the same rule the deployment uses can be supplied here and the
/// plan's "would overwrite" answer matches what would actually be written.
/// </summary>
public static class ImportPlanner
{
    public const string AnalysisMissingReason = "analysis_missing";

    public static ImportPlan Build(
        AnalysisResult? analysis,
        IEnumerable<string>? existingDeployNames = null,
        Func<PackageComponent, string>? deployNameOf = null)
    {
        if (analysis is null)
            return new ImportPlan(
                Array.Empty<ImportPlanComponent>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                RequiresConfirmation: false,
                RejectReason: AnalysisMissingReason);

        var decisions = analysis.RequiresUserDecision ?? Array.Empty<string>();
        var warnings = analysis.Warnings ?? Array.Empty<string>();

        if (analysis.RejectReason is not null)
            return new ImportPlan(
                Array.Empty<ImportPlanComponent>(),
                warnings,
                decisions,
                RequiresConfirmation: false,
                RejectReason: analysis.RejectReason);

        var existing = new HashSet<string>(
            existingDeployNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        var components = (analysis.Components ?? Array.Empty<PackageComponent>())
            .Select(component =>
            {
                var deployName = (deployNameOf?.Invoke(component) ?? component.Name ?? "").Trim();
                return new ImportPlanComponent(
                    component.Name ?? "",
                    component.Kind,
                    component.RootPath ?? "",
                    deployName,
                    DecideAction(component, deployName, existing));
            })
            .ToList();

        // Confirm whenever something would be replaced or the analyzer flagged uncertainty.
        var needsConfirmation = decisions.Count > 0 ||
                                components.Any(c => c.Action != ImportAction.Create);

        return new ImportPlan(components, warnings, decisions, needsConfirmation, RejectReason: null);
    }

    private static ImportAction DecideAction(
        PackageComponent component,
        string deployName,
        HashSet<string> existing)
    {
        // Nothing without a name can be placed, and an unsupported kind has no target domain.
        if (deployName.Length == 0 || component.Kind == ComponentKind.Unsupported)
            return ImportAction.Unsupported;

        return existing.Contains(deployName) ? ImportAction.Update : ImportAction.Create;
    }
}
