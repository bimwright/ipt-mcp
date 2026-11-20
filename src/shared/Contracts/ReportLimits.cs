using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Limits of <c>report_task_result</c> (family contract FC-RPT-01, FC-RPT-04; rvt-mcp ReportLimits parity), shared by
/// the gateway, which saves the report, and the add-in, which shows its card.
/// </summary>
public static class ReportLimits
{
    public const int TaskIdLength = 80;
    public const int SummaryLength = 120;
    public const int BodyBytes = 32 * 1024;

    /// <summary>A nonempty single-line string of at most <paramref name="max"/> characters, trimmed; else null.</summary>
    public static string? SingleLine(JToken? token, int max)
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

    /// <summary>Null when the report is valid, else the reason.</summary>
    public static string? Validate(JToken? taskId, JToken? outcome, JToken? summary, string? body)
    {
        if (SingleLine(taskId, TaskIdLength) == null) return "task_id must be a single-line string of 1-80 characters.";
        var o = SingleLine(outcome, 20);
        if (o != "completed" && o != "failed" && o != "cancelled") return "outcome must be completed, failed or cancelled.";
        if (SingleLine(summary, SummaryLength) == null)
            return "summary must be a single-line string of 1-120 characters" +
                   (summary?.Type == JTokenType.String ? $" (got {((string)summary!).Length}); put detail in body." : ".");
        if (body != null && Encoding.UTF8.GetByteCount(body) > BodyBytes) return "body must not exceed 32768 UTF-8 bytes.";
        return null;
    }
}
