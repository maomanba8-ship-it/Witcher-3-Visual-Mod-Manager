using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Import;
using Xunit;

namespace Witcher3ModManager.Tests;

/// <summary>
/// Stage 20a: the analyzer's grouping and the legacy folder-name scan are run side by side over
/// every structure fixture. The switch to the analyzer is only safe once the two agree everywhere
/// except where a difference is deliberate and documented.
/// </summary>
public class ImportCandidateComparisonTests
{
    /// <summary>Differences that are intended, with the reason they exist.</summary>
    private static readonly Dictionary<string, string> KnownDifferences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["weak-evidence-mod"] =
            "the analyzer reports components found only by their markers (and asks about them); " +
            "the legacy scan requires a standard name or container and skips them"
    };

    public static IEnumerable<object[]> FixtureDirs() => PackageAnalyzerTests.FixtureDirs();

    [Theory]
    [MemberData(nameof(FixtureDirs))]
    public void The_analyzer_and_the_legacy_scan_agree_on_the_fixtures(string fixtureDir)
    {
        var name = Path.GetFileName(fixtureDir);
        var legacy = LegacyCandidateScanner.Scan(fixtureDir);
        var analysis = PackageAnalyzer.Analyze(fixtureDir);
        var set = ImportCandidates.From(analysis);

        var legacyMods = Roots(legacy, wantDlc: false);
        var legacyDlcs = Roots(legacy, wantDlc: true);
        var analyzerMods = set.Mods.Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var analyzerDlcs = set.Dlcs.Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        if (KnownDifferences.ContainsKey(name))
        {
            // The documented difference has to actually be the documented one.
            Assert.True(analyzerMods.Count > legacyMods.Count || analyzerDlcs.Count > legacyDlcs.Count,
                $"{name}: expected the analyzer to find a component the legacy scan skips");
            return;
        }

        Assert.Equal(legacyMods, analyzerMods);
        Assert.Equal(legacyDlcs, analyzerDlcs);

        // The bin side: same file count, because both walk the same bin roots. Names are compared
        // as a multiset so the order of directory listings cannot make the test flaky.
        var legacyBinFiles = LegacyCandidateScanner.ScanBinFiles(fixtureDir);
        var analysisBinFiles = set.BinRoots
            .SelectMany(root => ImportCandidates.RelativePathsUnder(root))
            .ToList();

        Assert.Equal(legacyBinFiles.Count, analysisBinFiles.Count);
        Assert.Equal(
            legacyBinFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
            analysisBinFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }

    private static List<string> Roots(IEnumerable<LegacyCandidate> candidates, bool wantDlc) =>
        candidates
            .SelectMany(c => new[] { c.Root }.Concat(wantDlc ? c.AttachedDlcs : c.AttachedMods))
            .Where(dir => LegacyCandidateScanner.Classify(dir).IsDlc == wantDlc)
            .Select(dir => dir.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
