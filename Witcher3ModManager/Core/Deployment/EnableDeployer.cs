using System.IO;

namespace Witcher3ModManager.Core.Deployment;

public enum DeploymentStepOutcome
{
    Done,
    Skipped,
    Failed
}

/// <summary>
/// What happened to one planned step. BackupPath is set when something previously at the target
/// was moved aside, which is what makes the step reversible.
/// </summary>
public sealed record DeploymentStepResult(
    string Target,
    EnableAction Action,
    DeploymentStepOutcome Outcome,
    string? BackupPath = null,
    string? Detail = null);

/// <summary>
/// The result of one deployment. When it failed, RollbackProblems names anything that could not
/// be undone: an operation that cannot restore the previous state must say so rather than claim
/// it is fine.
/// </summary>
public sealed record DeploymentResult(
    bool Ok,
    IReadOnlyList<DeploymentStepResult> Steps,
    string? Failure,
    IReadOnlyList<string> RollbackProblems)
{
    public IReadOnlyList<DeploymentStepResult> Backups =>
        Steps.Where(s => !string.IsNullOrEmpty(s.BackupPath)).ToList();
}

/// <summary>
/// Carries out an enable plan. It makes no decisions of its own: the plan already says which
/// folders are involved and what has to happen to each.
///
/// The one thing this adds over the previous inline code is that nothing is ever deleted outright.
/// When a target is occupied, whatever is there is moved into this operation's staging folder
/// first, and every completed step registers how to undo itself, so a failure part-way through
/// can put things back instead of leaving half an enable behind.
/// </summary>
public sealed class EnableDeployer
{
    private readonly string _stagingRoot;

    public EnableDeployer(string stagingRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        _stagingRoot = stagingRoot;
    }

    public DeploymentResult Execute(EnablePlan? plan, string? operationId = null)
    {
        var steps = new List<DeploymentStepResult>();
        var undos = new List<(Action Undo, string Description)>();
        var staging = Path.Combine(_stagingRoot, string.IsNullOrWhiteSpace(operationId) ? "unidentified" : operationId);
        var work = (plan ?? EnablePlan.Empty).Work;
        string? failure = null;

        foreach (var step in work)
        {
            try
            {
                steps.Add(ExecuteStep(step, staging, undos));
            }
            catch (Exception ex)
            {
                steps.Add(new DeploymentStepResult(step.TargetPath, step.Action,
                    DeploymentStepOutcome.Failed, Detail: ex.Message));
                failure = $"{step.Action} failed for {step.TargetPath}: {ex.Message}";
                break;
            }
        }

        if (failure is null)
        {
            // The operation finished, so the copies kept for undoing it are no longer needed.
            TryRemoveStaging(staging);
            return new DeploymentResult(true, steps, null, Array.Empty<string>());
        }

        // The staging folder is deliberately left in place: it holds whatever was moved aside,
        // which is exactly what a later rollback needs.
        return new DeploymentResult(false, steps, failure, RollBack(undos));
    }

    private static DeploymentStepResult ExecuteStep(
        EnablePlanStep step,
        string staging,
        List<(Action Undo, string Description)> undos)
    {
        switch (step.Action)
        {
            case EnableAction.CopyFromStorage:
            {
                if (step.StorageSourcePath is null)
                    throw new InvalidOperationException("no storage source recorded for this step");

                MoveOrCopy(step.StorageSourcePath, step.TargetPath);
                undos.Add((() => RemoveCreated(step.TargetPath), $"remove the copy at {step.TargetPath}"));
                return new DeploymentStepResult(step.TargetPath, step.Action, DeploymentStepOutcome.Done);
            }

            case EnableAction.RestoreDisabledFolder:
            {
                string? backup = null;
                if (step.TargetOccupied)
                {
                    // Never delete what is sitting there: move it aside so this step can be undone.
                    backup = Path.Combine(staging, step.Domain.ToString(), step.Folder);
                    MoveOrCopy(step.TargetPath, backup);
                    undos.Add((() => MoveOrCopy(backup, step.TargetPath), $"put back {step.TargetPath}"));
                }

                MoveOrCopy(step.DisabledPath, step.TargetPath);
                undos.Add((() => MoveOrCopy(step.TargetPath, step.DisabledPath), $"re-disable {step.TargetPath}"));

                return new DeploymentStepResult(step.TargetPath, step.Action, DeploymentStepOutcome.Done, backup);
            }

            default:
                return new DeploymentStepResult(step.TargetPath, step.Action, DeploymentStepOutcome.Skipped);
        }
    }

    private static IReadOnlyList<string> RollBack(List<(Action Undo, string Description)> undos)
    {
        var problems = new List<string>();

        // Reverse order: the last thing done is the first thing undone.
        for (var i = undos.Count - 1; i >= 0; i--)
        {
            try
            {
                undos[i].Undo();
            }
            catch (Exception ex)
            {
                problems.Add($"{undos[i].Description}: {ex.Message}");
            }
        }

        return problems;
    }

    private static void RemoveCreated(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    private static void TryRemoveStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        catch
        {
            // Leaving an empty staging folder behind is harmless; failing the operation over it
            // would not be.
        }
    }

    /// <summary>
    /// Moves when it can and copies when it cannot, which keeps the common case cheap and still
    /// works when the staging folder lives on another volume than the game directory.
    /// </summary>
    private static void MoveOrCopy(string source, string destination)
    {
        if (!Directory.Exists(source) && !File.Exists(source))
            throw new DirectoryNotFoundException(source);

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"destination already exists: {destination}");

        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
            RemoveCreated(source);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
