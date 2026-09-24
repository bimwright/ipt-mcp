using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class TaskResultTests
{
    [Theory]
    [InlineData("task_id", "")]
    [InlineData("task_id", "line1\nline2")]
    [InlineData("summary", "   ")]
    [InlineData("outcome", "running")]
    [InlineData("outcome", "")]
    [InlineData("summary", null)]
    public void Invalid_reports_are_rejected(string field, string? value)
    {
        var p = new JObject { ["task_id"] = "task-42", ["outcome"] = "completed", ["summary"] = "Checked model" };
        p[field] = value;
        var result = new ReportTaskResultHandler().Execute(new InventorCommandContext(), p);
        Assert.False(result.Ok);
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, result.Error?.Code);
    }

    [Theory]
    [InlineData("task_id", 81)]
    [InlineData("summary", 121)]
    public void Report_text_is_bounded(string field, int length)
    {
        var p = new JObject { ["task_id"] = "task-42", ["outcome"] = "completed", ["summary"] = "Checked model" };
        p[field] = new string('x', length);
        Assert.False(new ReportTaskResultHandler().Execute(new InventorCommandContext(), p).Ok);
        p[field] = 42;
        Assert.False(new ReportTaskResultHandler().Execute(new InventorCommandContext(), p).Ok);
    }

    [Fact]
    public void Format_characters_like_bidi_overrides_are_rejected()
    {
        var p = new JObject
        {
            ["task_id"] = "task-42", ["outcome"] = "completed",
            ["summary"] = "draw " + (char)0x202E + " reversed",   // U+202E would render misleading text
        };
        var result = new ReportTaskResultHandler().Execute(new InventorCommandContext(), p);
        Assert.False(result.Ok);
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, result.Error?.Code);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void Agent_can_report_an_explicit_result_without_a_document_even_in_read_only_mode(string outcome)
    {
        var handler = new ReportTaskResultHandler();
        var dispatcher = new CommandDispatcher(new Dictionary<string, IInventorCommand> { [handler.Name] = handler }, 5_000_000);
        var result = dispatcher.Dispatch(new InventorCommandContext { ReadOnly = true }, new InventorCommandEnvelope
        {
            Id = Guid.NewGuid(), Command = "report_task_result",
            Params = new JObject { ["task_id"] = "agent-42", ["outcome"] = outcome, ["summary"] = "Checked assembly" },
        });
        Assert.True(result.Ok);
        Assert.Equal("agent-42", (string?)result.Data?["task_id"]);
        Assert.Equal(outcome, (string?)result.Data?["outcome"]);
        Assert.True((bool?)result.Data?["agent_reported"]);
    }
}
