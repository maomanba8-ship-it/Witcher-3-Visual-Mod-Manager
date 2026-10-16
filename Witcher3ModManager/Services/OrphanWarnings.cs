using Witcher3ModManager.Core.Deployment;
using Witcher3ModManager.Models;

namespace Witcher3ModManager.Services;

/// <summary>
/// Why an orphan was not taken into the library, worded for the user. The outcome carries facts
/// only, so the sentences live here with the interface language.
/// </summary>
public static class OrphanWarnings
{
    public static string Line(OrphanModInfo orphan, ModStorageService.AdoptionOutcome outcome)
    {
        var name = string.IsNullOrWhiteSpace(orphan.FolderName) ? "?" : orphan.FolderName;
        var reason = Reason(outcome);

        return string.Format(TranslationService.Instance.T("op.line"), name, reason);
    }

    private static string Reason(ModStorageService.AdoptionOutcome outcome)
    {
        if (outcome.Warnings.Count == 0)
            return TranslationService.Instance.T("orphan.adopt.reason.known");

        return string.Join("；", outcome.Warnings.Select(Warning));
    }

    private static string Warning(AdoptionWarning warning) => warning.Problem switch
    {
        AdoptionProblem.AlreadyOwned => string.Format(
            TranslationService.Instance.T("orphan.adopt.reason.owned"), warning.Owner ?? ""),
        AdoptionProblem.Unreadable => TranslationService.Instance.T("orphan.adopt.reason.unreadable"),
        _ => warning.Subject ?? ""
    };
}
