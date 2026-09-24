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
        var taskId = SingleLine(p["task_id"], 80);
        var summary = SingleLine(p["summary"], 120);
        var outcome = SingleLine(p["outcome"], 20);
        if (taskId == null || summary == null || (outcome != "completed" && outcome != "failed" && outcome != "cancelled"))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT,
                "task_id (1-80 chars) and summary (1-120 chars) must be nonempty single-line strings; outcome must be completed, failed or cancelled.", meta);
        return InventorCommandResult.Success(Guid.Empty, new JObject
        {
            ["task_id"] = taskId, ["outcome"] = outcome, ["summary"] = summary,
            ["agent_reported"] = true,
        }, meta);
    }

    private static string? SingleLine(JToken? token, int max)
    {
        if (token is not JValue { Type: JTokenType.String } value) return null;
        var s = (string?)value;
        if (string.IsNullOrWhiteSpace(s) || s!.Length > max) return null;
        // Format characters (Cf) include bidi overrides and invisible marks: a summary containing
        // U+202E could render misleading text on the toast, so reject rather than sanitize.
        foreach (var c in s)
            if (char.IsControl(c) || c == '\u2028' || c == '\u2029'
                || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
                return null;
        return s.Trim();
    }
}
