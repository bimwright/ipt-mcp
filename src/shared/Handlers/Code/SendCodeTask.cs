using Bimwright.Ipt.Scripting;
using Bimwright.Ipt.Shared.Security;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Code;

/// <summary>
/// <c>result_contract</c> for send_code (rvt-mcp parity): <c>legacy</c> keeps the old response; <c>task_v1</c>
/// requires the script to return <see cref="McpTaskResult"/> and reports the outcome in <c>_task</c>.
/// A task failure does not undo edits the script already committed.
/// </summary>
public static class SendCodeTask
{
    public const string Legacy = "legacy";
    public const string TaskV1 = "task_v1";
    public const string ScriptNamespace = "Bimwright.Ipt.Scripting";
    public const string TaskResultRequired = "TASK_RESULT_REQUIRED";
    private const int MaxCodeChars = 128;
    private const int MaxMessageChars = 512;

    public static bool TryReadContract(JToken? token, out string contract, out string? error)
    {
        contract = Legacy;
        error = null;
        if (token is null || token.Type == JTokenType.Null) return true;
        var text = token.Type == JTokenType.String ? (string?)token : null;
        if (text == Legacy || text == TaskV1)
        {
            contract = text;
            return true;
        }
        error = "result_contract must be \"legacy\" or \"task_v1\"";
        return false;
    }

    /// <summary>The value to serialize as <c>result</c>: under task_v1, Pass gives its value and Fail its details.</summary>
    public static object? ResultValue(string contract, object? returned) =>
        contract == TaskV1 && returned is McpTaskResult task ? (task.IsPassed ? task.Value : task.Details) : returned;

    /// <summary>
    /// Adds <c>_task</c> under task_v1 (<paramref name="completed"/>: the script ran to the end) and fails the
    /// response when the task failed or returned no <see cref="McpTaskResult"/>. Under legacy only adds an
    /// advisory hint when the result itself reports <c>passed/ok/success=false</c>.
    /// </summary>
    public static void Apply(JObject data, string contract, bool completed, object? returned)
    {
        if (contract != TaskV1)
        {
            if (completed) AttachLegacyHint(data);
            return;
        }

        var task = new JObject { ["contract"] = TaskV1 };
        data["_task"] = task;
        if (!completed)
        {
            task["status"] = "not_reported";
            return;
        }

        string code, message;
        if (returned is not McpTaskResult result)
        {
            task["status"] = "not_reported";
            code = TaskResultRequired;
            message = "Return McpTaskResult.Pass(value) or McpTaskResult.Fail(code, message) under result_contract=task_v1.";
        }
        else if (result.IsPassed)
        {
            task["status"] = "passed";
            return;
        }
        else
        {
            task["status"] = "failed";
            code = Bound(ErrorSanitizer.Sanitize(result.ErrorCode ?? "TASK_FAILED"), MaxCodeChars);
            message = Bound(ErrorSanitizer.Sanitize(result.ErrorMessage ?? ""), MaxMessageChars);
        }

        task["error"] = new JObject { ["code"] = code, ["message"] = message };
        var hadError = data["error"] is JValue { Type: JTokenType.String } e && !string.IsNullOrEmpty((string?)e);
        data["ok"] = false;
        if (!hadError) data["error"] = code + ": " + message;
    }

    private static void AttachLegacyHint(JObject data)
    {
        if (data["result"] is not JObject value) return;
        foreach (var name in new[] { "passed", "ok", "success" })
        {
            if (value[name]?.Type == JTokenType.Boolean && !(bool)value[name]!)
            {
                data["task_contract_hint"] = "The result reports " + name + "=false, but the call itself succeeded. " +
                    "Use result_contract=\"task_v1\" with McpTaskResult.Fail to report a failed task.";
                return;
            }
        }
    }

    private static string Bound(string text, int max) => text.Length <= max ? text : text.Substring(0, max);
}
