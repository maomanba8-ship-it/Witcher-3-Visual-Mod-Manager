using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Witcher3ModManager.Core.Migration;
using Witcher3ModManager.Services;

namespace Witcher3ModManager.Views;

/// <summary>
/// Read-only report of the library audit. It can only display and copy: no button here changes
/// anything, which is what makes it safe to open at any time. Acting on the findings belongs to
/// the migration wizard, which may only run after the user has seen this.
/// </summary>
public partial class LibraryAuditDialog : Window
{
    private static readonly LegacyAuditKind[] SectionOrder =
    {
        LegacyAuditKind.EnabledButMissingInGame,
        LegacyAuditKind.StorageFolderMissing,
        LegacyAuditKind.Unattributable,
        LegacyAuditKind.BinOnly,
        LegacyAuditKind.DuplicateTargetFolder,
        LegacyAuditKind.SharedFolder
    };

    private readonly LegacyLibraryAudit _audit;
    private IDisposable? _languageSubscription;

    public LibraryAuditDialog(LegacyLibraryAudit audit)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));

        BuildReport();
        Translate();

        _languageSubscription = TranslationService.Instance.Subscribe(() => Dispatcher.Invoke(() =>
        {
            Translate();
            BuildReport();
        }));
        Closed += (_, _) =>
        {
            _languageSubscription?.Dispose();
            _languageSubscription = null;
        };
    }

    private void Translate()
    {
        var t = TranslationService.Instance;
        Title = t.T("audit.title");
        TitleText.Text = t.T("audit.title");
        NoteText.Text = t.T("audit.note");
        CopyBtn.Content = t.T("audit.copy");
        CloseBtn.Content = t.T("dialog.close");
        CopyStatusText.Text = t.T("audit.copied");
    }

    private void BuildReport()
    {
        var t = TranslationService.Instance;
        SummaryText.Text = string.Format(t.T("audit.summary"), _audit.EntryCount, _audit.Findings.Count);
        CleanText.Text = t.T("audit.clean");
        CleanPanel.Visibility = _audit.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ReportStack.Children.Clear();
        foreach (var kind in SectionOrder)
        {
            var findings = _audit.OfKind(kind);
            if (findings.Count == 0) continue;
            ReportStack.Children.Add(BuildSection(t, kind, findings));
        }
    }

    private static Border BuildSection(TranslationService t, LegacyAuditKind kind, IReadOnlyList<LegacyAuditFinding> findings)
    {
        var accent = kind == LegacyAuditKind.SharedFolder ? "#6BA5B5" : "#C9A96E";
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(t.T("audit.section"), t.T(KindKey(kind)), findings.Count),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(accent),
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var finding in findings)
        {
            panel.Children.Add(new TextBlock
            {
                Text = DescribeFinding(t, finding),
                FontSize = 12,
                Foreground = Brush("#C8D0D8"),
                Margin = new Thickness(12, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Background = Brush("#1814161E"),
            BorderBrush = Brush("#1E2230"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }

    private static string DescribeFinding(TranslationService t, LegacyAuditFinding finding)
    {
        var entries = string.Join(", ", finding.Entries);
        var subjects = string.Join(", ", finding.Subjects);

        return finding.Kind switch
        {
            LegacyAuditKind.Unattributable => string.Format(t.T("audit.line.unattributable"), entries),
            LegacyAuditKind.BinOnly => string.Format(t.T("audit.line.bin_only"), entries, finding.Subjects.Count),
            LegacyAuditKind.StorageFolderMissing => string.Format(t.T("audit.line.storage_missing"), entries, subjects),
            LegacyAuditKind.DuplicateTargetFolder => string.Format(t.T("audit.line.duplicate"), entries, subjects),
            LegacyAuditKind.SharedFolder => string.Format(t.T("audit.line.shared"), subjects, entries),
            LegacyAuditKind.EnabledButMissingInGame => string.Format(t.T("audit.line.enabled_missing"), entries, subjects),
            _ => entries
        };
    }

    private static string KindKey(LegacyAuditKind kind) => kind switch
    {
        LegacyAuditKind.Unattributable => "audit.kind.unattributable",
        LegacyAuditKind.BinOnly => "audit.kind.bin_only",
        LegacyAuditKind.StorageFolderMissing => "audit.kind.storage_missing",
        LegacyAuditKind.DuplicateTargetFolder => "audit.kind.duplicate",
        LegacyAuditKind.SharedFolder => "audit.kind.shared",
        LegacyAuditKind.EnabledButMissingInGame => "audit.kind.enabled_missing",
        _ => "audit.kind.other"
    };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        var lines = new List<string> { TitleText.Text, SummaryText.Text, "" };
        foreach (var kind in SectionOrder)
        {
            var findings = _audit.OfKind(kind);
            if (findings.Count == 0) continue;
            lines.Add(string.Format(TranslationService.Instance.T("audit.section"),
                TranslationService.Instance.T(KindKey(kind)), findings.Count));
            foreach (var finding in findings)
                lines.Add("  " + DescribeFinding(TranslationService.Instance, finding));
            lines.Add("");
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            CopyStatusText.Visibility = Visibility.Visible;
        }
        catch
        {
            // The clipboard can be busy; the report stays on screen either way.
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
