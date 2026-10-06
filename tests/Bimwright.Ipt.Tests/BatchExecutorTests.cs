using Bimwright.Ipt.Shared.Handlers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// F4-P1: batch iteration semantics — max-20 cap, blocked command names, stop-at-first-error
/// vs continue_on_error, per-step result shape. The transaction lifecycle and real dispatch
/// live in BatchExecuteHandler (plugin-side); here the invoke lambda is a stub.
/// </summary>
public sealed class BatchExecutorTests
{
    private static JArray Cmds(params object[] items)
        => new(items.Select(i => JObject.FromObject(i)).Cast<JToken>().ToArray());

    [Fact]
    public void Happy_path_runs_every_step_and_collects_data()
    {
        var cmds = Cmds(
            new { command = "create_sketch", @params = new { plane = "XY" } },
            new { command = "close_sketch" });

        var outcome = BatchExecutor.Run(cmds, false,
            (name, p) => BatchExecutor.StepResult.Success(new JObject { ["echo"] = name }));

        Assert.False(outcome.AnyFailed);
        Assert.Equal(2, outcome.Results.Count);
        Assert.Equal("create_sketch", (string?)outcome.Results[0]?["data"]?["echo"]);
        Assert.Equal("close_sketch", (string?)outcome.Results[1]?["data"]?["echo"]);
    }

    [Fact]
    public void Over_20_commands_fails_upfront()
    {
        var cmds = new JArray(Enumerable.Range(0, 21).Select(_ => JObject.FromObject(new { command = "noop" })));
        var invoked = 0;

        var outcome = BatchExecutor.Run(cmds, true, (_, _) => { invoked++; return BatchExecutor.StepResult.Success(null); });

        Assert.True(outcome.AnyFailed);
        Assert.Equal(0, invoked);
        Assert.Single(outcome.Results);
        Assert.Contains("at most 20", (string?)outcome.Results[0]?["error"]);
    }

    [Theory]
    [InlineData("batch_execute")]
    [InlineData("Batch_Execute")]        // registry is OrdinalIgnoreCase — so is the block list
    [InlineData("send_code")]
    [InlineData("SEND_CODE")]
    [InlineData("run_baked_tool")]
    [InlineData("apply_bake")]
    [InlineData("new_part")]             // document lifecycle — would move later steps onto another doc
    [InlineData("new_assembly")]
    [InlineData("open_document")]
    [InlineData("close_document")]
    [InlineData("save_document")]
    [InlineData("derive_envelope")]     // creates + activates a new document mid-transaction
    public void Blocked_commands_are_rejected_without_invoking(string blocked)
    {
        var cmds = Cmds(new { command = blocked });
        var invoked = 0;

        var outcome = BatchExecutor.Run(cmds, false, (_, _) => { invoked++; return BatchExecutor.StepResult.Success(null); });

        Assert.True(outcome.AnyFailed);
        Assert.Equal(0, invoked);
        Assert.Contains("cannot run inside batch_execute", (string?)outcome.Results[0]?["error"]);
    }

    [Fact]
    public void Stops_at_first_error_by_default()
    {
        var cmds = Cmds(
            new { command = "good" },
            new { command = "bad" },
            new { command = "never" });

        var outcome = BatchExecutor.Run(cmds, false, (name, _) =>
            name == "bad"
                ? BatchExecutor.StepResult.Failure("boom")
                : BatchExecutor.StepResult.Success(null));

        Assert.True(outcome.AnyFailed);
        Assert.Equal(2, outcome.Results.Count);         // 'never' never ran
        Assert.Equal("boom", (string?)outcome.Results[1]?["error"]);
    }

    [Fact]
    public void Continue_on_error_runs_remaining_steps()
    {
        var cmds = Cmds(
            new { command = "good" },
            new { command = "bad" },
            new { command = "also_good" });

        var outcome = BatchExecutor.Run(cmds, true, (name, _) =>
            name == "bad"
                ? BatchExecutor.StepResult.Failure("boom")
                : BatchExecutor.StepResult.Success(null));

        Assert.True(outcome.AnyFailed);
        Assert.Equal(3, outcome.Results.Count);
        Assert.True((bool?)outcome.Results[2]?["ok"]);
    }

    [Fact]
    public void Missing_command_field_is_a_step_failure()
    {
        var cmds = Cmds(new { @params = new { a = 1 } });

        var outcome = BatchExecutor.Run(cmds, false, (_, _) => BatchExecutor.StepResult.Success(null));

        Assert.True(outcome.AnyFailed);
        Assert.Contains("missing 'command'", (string?)outcome.Results[0]?["error"]);
    }

    [Fact]
    public void Invoke_exceptions_become_step_failures_not_throws()
    {
        var cmds = Cmds(new { command = "explode" });

        var outcome = BatchExecutor.Run(cmds, false, (_, _) => throw new InvalidOperationException("kaboom"));

        Assert.True(outcome.AnyFailed);
        Assert.Contains("kaboom", (string?)outcome.Results[0]?["error"]);
    }

    [Fact]
    public void Non_object_params_is_a_step_failure_not_silent_empty()
    {
        var cmds = Cmds(new { command = "x", @params = "not-an-object" });

        var invoked = 0;
        var outcome = BatchExecutor.Run(cmds, false, (_, _) => { invoked++; return BatchExecutor.StepResult.Success(null); });

        Assert.True(outcome.AnyFailed);
        Assert.Equal(0, invoked);
        Assert.Contains("'params' must be an object", (string?)outcome.Results[0]?["error"]);
    }

    [Fact]
    public void Invoke_receives_params_and_empty_object_by_default()
    {
        JObject? seen = null;
        var cmds = Cmds(new { command = "x", @params = new { n = 7 } });
        BatchExecutor.Run(cmds, false, (_, p) => { seen = p; return BatchExecutor.StepResult.Success(null); });
        Assert.Equal(7, (int?)seen?["n"]);

        var cmds2 = Cmds(new { command = "x" });
        BatchExecutor.Run(cmds2, false, (_, p) => { seen = p; return BatchExecutor.StepResult.Success(null); });
        Assert.Empty(seen!);
    }
}
