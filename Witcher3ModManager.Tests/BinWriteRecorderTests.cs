using System;
using System.Collections.Generic;
using Witcher3ModManager.Core.Analysis;
using Witcher3ModManager.Core.Deployment;
using Xunit;

namespace Witcher3ModManager.Tests;

public class BinWriteRecorderTests
{
    private const string InputXml = "config/r4game/user_config_matrix/pc/input.xml";
    private const string Target = @"C:\game\bin\config\r4game\user_config_matrix\pc\input.xml";
    private const string Source = @"C:\data\bin\pkg\config\r4game\user_config_matrix\pc\input.xml";

    [Fact]
    public void A_copy_that_ran_is_recorded_with_what_it_replaced()
    {
        var writes = BinWriteRecorder.From(Result(Done()), Captured(InputXml, "original"), Hash("written"));

        var write = Assert.Single(writes);
        Assert.Equal(InputXml, write.Path);
        Assert.Equal("written", write.Sha256);
        Assert.Equal("original", write.ShaBefore);
    }

    [Fact]
    public void A_file_that_was_not_there_before_is_recorded_as_such()
    {
        var writes = BinWriteRecorder.From(Result(Done()), Captured(InputXml, null), Hash("written"));

        Assert.Null(Assert.Single(writes).ShaBefore);
    }

    [Fact]
    public void Steps_that_did_not_run_are_not_recorded()
    {
        // A skip means we never copied anything: claiming it would make a later stop try to
        // "restore" a file we never wrote.
        var skipped = new DeploymentStepResult(TargetDomain.Bin, InputXml, Target,
            DeploymentAction.Copy, DeploymentStepOutcome.Skipped);

        Assert.Empty(BinWriteRecorder.From(Result(skipped), Captured(InputXml, "original"), Hash("written")));
    }

    [Fact]
    public void Folder_steps_are_not_bin_writes()
    {
        var folder = new DeploymentStepResult(TargetDomain.Mods, "modA", @"C:\game\mods\modA",
            DeploymentAction.Copy, DeploymentStepOutcome.Done);

        Assert.Empty(BinWriteRecorder.From(Result(folder), Captured(InputXml, "original"), Hash("written")));
    }

    [Fact]
    public void Without_a_captured_state_nothing_is_claimed()
    {
        Assert.Empty(BinWriteRecorder.From(Result(Done()), Captured("config/other.xml", "x"), Hash("written")));
        Assert.Empty(BinWriteRecorder.From(Result(Done()), new Dictionary<string, string?>(), Hash("written")));
    }

    [Fact]
    public void The_source_is_used_when_the_written_file_cannot_be_read_back()
    {
        // This is the case that once left a successful enable with no record at all: what was
        // copied *is* the source's bytes, so the source answers when the game copy does not.
        var writes = BinWriteRecorder.From(Result(Done()),
            Captured(InputXml, "original"),
            path => path == Source ? "fromSource" : null);

        Assert.Equal("fromSource", Assert.Single(writes).Sha256);
    }

    [Fact]
    public void Without_a_hash_from_either_side_nothing_is_claimed()
    {
        Assert.Empty(BinWriteRecorder.From(Result(Done()), Captured(InputXml, "original"), _ => null));
    }

    [Fact]
    public void Without_a_result_or_captured_state_there_is_nothing_to_record()
    {
        Assert.Empty(BinWriteRecorder.From(null, Captured(InputXml, "original"), Hash("written")));
        Assert.Empty(BinWriteRecorder.From(Result(Done()), null, Hash("written")));
    }

    private static DeploymentStepResult Done() =>
        new(TargetDomain.Bin, InputXml, Target, DeploymentAction.Copy, DeploymentStepOutcome.Done,
            SourcePath: Source);

    private static DeploymentResult Result(params DeploymentStepResult[] steps) =>
        new(true, steps, null, Array.Empty<string>());

    private static Dictionary<string, string?> Captured(string path, string? before) =>
        new(StringComparer.OrdinalIgnoreCase) { [path] = before };

    private static Func<string, string?> Hash(string value) => _ => value;
}
