using System.IO;
using Witcher3ModManager.Core.Analysis;

namespace Witcher3ModManager.Core.Deployment;

public enum DeploymentStepOutcome
{
    Done,
    Skipped,
    Failed
}

/// <summary>
/// What happened to one step. Domain and Subject name the component the step was about, so a report
/// can point at "mods\modA" instead of at a path: for a stash step the path would be a staging
/// folder the user has no reason to know about.
///
/// Target is the location the step worked on (its destination, or the source when there is no
/// destination to name); SourcePath says where the content came from. BackupPath is set when
/// something previously at the destination was moved aside, which is what makes the step
/// reversible.
/// </summary>
public sealed record DeploymentStepResult(
    TargetDomain Domain,
    string Subject,
    string Target,
    DeploymentAction Action,
    DeploymentStepOutcome Outcome,
    string? BackupPath = null,
    string? Detail = null,
    string? SourcePath = null);

/// <summary>
/// The result of one deployment. When it failed, RollbackProblems names anything that could not be
/// undone: an operation that cannot restore the previous state has to say so.
/// </summary>
public sealed record DeploymentResult(
    bool Ok,
    IReadOnlyList<DeploymentStepResult> Steps,
    string? Failure,
    IReadOnlyList<string> RollbackProblems,
    string? StagingLeftBehind = null)
{
    public IReadOnlyList<DeploymentStepResult> Backups =>
        Steps.Where(s => !string.IsNullOrEmpty(s.BackupPath)).ToList();
}

/// <summary>
/// Carries out a plan. It makes no decisions: the plan already says what happens where.
///
/// Two rules apply to every operation that goes through here, which is the point of having one
/// engine instead of one code path per operation:
///
/// 1. Nothing at a destination is ever deleted outright. It is moved into this operation's staging
///    area first, so the step can be undone and the content is not gone until the operation as a
///    whole has succeeded.
/// 2. Every completed step registers how to undo itself, so a failure part-way through puts things
///    back instead of leaving half an operation behind.
/// </summary>
public sealed class DeploymentEngine
{
    private readonly string _stagingRoot;

    public DeploymentEngine(string stagingRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        _stagingRoot = stagingRoot;
    }

    public DeploymentResult Execute(DeploymentPlan? plan, string? operationId = null)
    {
        var steps = new List<DeploymentStepResult>();
        var undos = new List<(Action Undo, string Description)>();
        var staging = Path.Combine(_stagingRoot, string.IsNullOrWhiteSpace(operationId) ? "unidentified" : operationId);
        string? failure = null;

        foreach (var step in (plan ?? DeploymentPlan.Empty).Steps)
        {
            if (step.Action == DeploymentAction.NothingToDo)
            {
                steps.Add(new DeploymentStepResult(step.Domain, step.Subject, step.DestinationPath,
                    step.Action, DeploymentStepOutcome.Skipped, SourcePath: step.SourcePath));
                continue;
            }

            try
            {
                steps.Add(ExecuteStep(step, staging, undos));
            }
            catch (Exception ex)
            {
                // A stash step has no destination of its own, so the source is the location worth
                // naming in the report.
                var where = string.IsNullOrEmpty(step.DestinationPath) ? step.SourcePath : step.DestinationPath;
                steps.Add(new DeploymentStepResult(step.Domain, step.Subject, where, step.Action,
                    DeploymentStepOutcome.Failed, Detail: ex.Message, SourcePath: step.SourcePath));
                failure = $"{step.Action} failed for {where}: {ex.Message}";
                break;
            }
        }

        if (failure is null)
        {
            // The operation finished, so the copies kept for undoing it are no longer needed.
            var leftBehind = TryRemoveStaging(staging);
            return new DeploymentResult(true, steps, null, Array.Empty<string>(), leftBehind);
        }

        // The staging folder is deliberately left in place: it holds whatever was moved aside,
        // which is exactly what a later rollback needs.
        return new DeploymentResult(false, steps, failure, RollBack(undos));
    }

    private static DeploymentStepResult ExecuteStep(
        DeploymentStep step,
        string staging,
        List<(Action Undo, string Description)> undos)
    {
        var destination = step.Action == DeploymentAction.Stash
            ? Path.Combine(staging, step.Domain.ToString(), step.Subject)
            : step.DestinationPath;

        // Whatever is already at the destination goes to staging rather than being deleted.
        string? backup = null;
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            backup = Path.Combine(staging, "occupied", step.Domain.ToString(), step.Subject);
            MoveOrCopy(destination, backup);
            undos.Add((() => MoveOrCopy(backup, destination), $"put back {destination}"));
        }

        if (step.Action == DeploymentAction.Copy)
            CopyAnything(step.SourcePath, destination);
        else
            MoveOrCopy(step.SourcePath, destination);

        undos.Add(step.Action == DeploymentAction.Copy
            ? (() => RemoveCreated(destination), $"remove the copy at {destination}")
            : (() => MoveOrCopy(destination, step.SourcePath), $"move {destination} back"));

        return new DeploymentStepResult(step.Domain, step.Subject, destination, step.Action,
            DeploymentStepOutcome.Done, backup, SourcePath: step.SourcePath);
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

    /// <summary>
    /// Clears the staging area of an operation that finished. Returns the path when it could not be
    /// cleared: content still sitting in there is worth naming, because the operation is reported
    /// as done while something it moved aside is still on disk.
    /// </summary>
    private static string? TryRemoveStaging(string staging)
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

        return Directory.Exists(staging) ? staging : null;
    }

    /// <summary>
    /// Moves when it can and copies when it cannot, so the common case (staging beside the game
    /// directory) costs a rename and a cross-volume case still works.
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
            if (File.Exists(source)) File.Move(source, destination);
            else Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyAnything(source, destination);
            RemoveCreated(source);
        }
    }

    /// <summary>
    /// Copies a folder or a single file. Bin deployment moves individual files, so a step whose
    /// source is a file has to work exactly like a folder step: the same staging, the same undo.
    /// </summary>
    private static void CopyAnything(string source, string destination)
    {
        if (File.Exists(source))
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.Copy(source, destination, overwrite: true);
            return;
        }

        CopyDirectory(source, destination);
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
