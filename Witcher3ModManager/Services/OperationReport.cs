using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;

namespace Witcher3ModManager.Services;

/// <summary>
/// Turns an operation's outcome into the text a dialog shows. The outcome carries facts only, so
/// every sentence here comes from the translation table and follows the interface language.
/// </summary>
public static class OperationReport
{
    /// <summary>
    /// The whole message: the caller's already-translated heading and the subject, then whatever
    /// there is to say. The heading is passed in rather than looked up here so the call site stays
    /// recognisable to the guard that checks dialog text goes through the translation table.
    /// </summary>
    public static string Text(string header, string subject, OperationOutcome? outcome)
    {
        var title = header + " " + subject;
        var details = Details(outcome);
        return details.Length == 0 ? title : title + "\n\n" + details;
    }

    /// <summary>
    /// The part a report has to add, one line per fact, in the order the operation met them. Empty
    /// when there is nothing to report.
    /// </summary>
    public static string Details(OperationOutcome? outcome)
    {
        if (outcome is null) return "";
        var t = TranslationService.Instance;
        var lines = new List<string>();

        foreach (var note in outcome.Notes.Where(n => n.Kind == OperationNoteKind.StepFailed))
            lines.Add(Bullet(Line(Folder(note), note.Detail ?? "")));

        // A failure that is not attributable to a single step still has to be said out loud.
        if (lines.Count == 0 && !outcome.Ok && !string.IsNullOrWhiteSpace(outcome.Failure))
            lines.Add(outcome.Failure!);

        AddGroup(lines, t.T("op.rollback_problems"),
            outcome.Notes.Where(n => n.Kind == OperationNoteKind.RollbackProblem).Select(n => n.Detail ?? ""));
        AddGroup(lines, t.T("op.not_removed"),
            outcome.Notes.Where(n => n.Kind == OperationNoteKind.PathNotRemoved).Select(n => n.Detail ?? ""));
        AddGroup(lines, t.T("op.no_source"),
            outcome.WithoutSource.Select(Folder));

        AddGroup(lines, t.T("op.bin_kept"),
            outcome.Notes
                .Where(n => n.Kind is OperationNoteKind.BinModifiedElsewhere
                    or OperationNoteKind.BinNoRecord
                    or OperationNoteKind.BinAlreadyGone
                    or OperationNoteKind.BinBackupMissing)
                .Select(n => Line(Folder(n), t.T(BinReasonKey(n.Kind)))));

        AddGroup(lines, t.T("op.staging_left"),
            outcome.Notes.Where(n => n.Kind == OperationNoteKind.StagingLeftBehind)
                .Select(n => n.Detail ?? ""));

        if (outcome.RolledBack) lines.Add(t.T("op.rolled_back"));

        return string.Join("\n", lines);
    }

    /// <summary>One line naming a subject and its problem, for lists that combine several operations.</summary>
    public static string OneLine(string subject, OperationOutcome? outcome)
    {
        var details = Details(outcome).Replace("\n", " ").Trim();
        return details.Length == 0 ? subject : subject + " - " + details;
    }

    private static void AddGroup(List<string> lines, string heading, IEnumerable<string> items)
    {
        var bullets = items.Where(i => i.Length > 0).Select(Bullet).ToList();
        if (bullets.Count == 0) return;

        lines.Add(heading);
        lines.AddRange(bullets);
    }

    private static string Line(string left, string right) =>
        string.Format(TranslationService.Instance.T("op.line"), left, right);

    private static string BinReasonKey(OperationNoteKind kind) => kind switch
    {
        OperationNoteKind.BinNoRecord => "op.bin.reason.no_record",
        OperationNoteKind.BinAlreadyGone => "op.bin.reason.gone",
        OperationNoteKind.BinBackupMissing => "op.bin.reason.no_backup",
        _ => "op.bin.reason.modified"
    };

    /// <summary>
    /// The component as the user would look for it on disk: "mods\modA", "dlc\dlcA" or
    /// "bin\config\...\input.xml".
    /// </summary>
    private static string Folder(OperationNote note) => note.Domain switch
    {
        TargetDomain.Dlc => "dlc\\" + (note.Subject ?? ""),
        TargetDomain.Bin => "bin\\" + (note.Subject ?? "").Replace('/', '\\'),
        _ => "mods\\" + (note.Subject ?? "")
    };

    private static string Bullet(string text) => "· " + text;
}
