using System;
using System.Globalization;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// Explicit agent-reported outcome; does not infer completion, inspect or modify any document.
/// The normal post-response notification hook turns the returned DTO into a summary card.
/// STA-independent: the command touches no Inventor API, so the add-in answers it on the listener
/// thread and it still reports while the STA is jammed.
/// </summary>
public sealed class ReportTaskResultHandler : IStaIndependentCommand
{
    public string Name => "report_task_result";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };
        var taskId = ReportLimits.SingleLine(p["task_id"], ReportLimits.TaskIdLength);
        var summary = ReportLimits.SingleLine(p["summary"], ReportLimits.SummaryLength);
        var outcome = ReportLimits.SingleLine(p["outcome"], 20);
        if (taskId == null || summary == null || (outcome != "completed" && outcome != "failed" && outcome != "cancelled"))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT,
                "task_id (1-80 chars) and summary (1-120 chars) must be nonempty single-line strings; outcome must be completed, failed or cancelled.", meta);
        return InventorCommandResult.Success(Guid.Empty, new JObject
        {
            ["task_id"] = taskId, ["outcome"] = outcome, ["summary"] = summary,
            ["agent_reported"] = true,
        }, meta);
    }

}
