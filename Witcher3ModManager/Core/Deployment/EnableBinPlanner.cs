using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

/// <summary>
/// Works out which bin files an enable would copy into the game, before anything is touched.
///
/// A bin file is one file in one place, so this planner only has to answer "is there a copy to
/// deploy from?". What was there before, and who else has written, is the ledger's business, not
/// the planner's: it says what to do, not what to remember.
/// </summary>
public static class EnableBinPlanner
{
    public static DeploymentPlan Plan(
        string? gameBinRoot,
        string? storageBinRoot,
        IEnumerable<string>? relativePaths,
        Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(gameBinRoot) || string.IsNullOrWhiteSpace(storageBinRoot))
            return DeploymentPlan.Empty;

        var exists = fileExists ?? File.Exists;
        var steps = new List<DeploymentStep>();

        foreach (var relative in Distinct(relativePaths))
        {
            var osPath = relative.Replace('/', Path.DirectorySeparatorChar);
            var source = Path.Combine(storageBinRoot, osPath);
            var destination = Path.Combine(gameBinRoot, osPath);

            steps.Add(exists(source)
                ? new DeploymentStep(TargetDomain.Bin, relative, DeploymentAction.Copy, source, destination)
                : new DeploymentStep(TargetDomain.Bin, relative, DeploymentAction.NothingToDo, source, destination));
        }

        return steps.Count == 0 ? DeploymentPlan.Empty : new DeploymentPlan(steps);
    }

    /// <summary>
    /// Paths are compared the way Windows compares them, and are normalised here so the step's
    /// subject is the same string the ledger keys its records by.
    /// </summary>
    private static IEnumerable<string> Distinct(IEnumerable<string>? paths) =>
        (paths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(BinLedger.Normalize)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
}
