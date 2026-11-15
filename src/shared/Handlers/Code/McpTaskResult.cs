namespace Bimwright.Ipt.Scripting;

/// <summary>
/// Explicit task result for <c>send_code</c> with <c>result_contract="task_v1"</c> (rvt-mcp parity).
/// The script returns <see cref="Pass"/> or <see cref="Fail"/>; the add-in's run/transaction outcome stays separate.
/// The namespace is imported into task_v1 scripts only.
/// </summary>
public sealed class McpTaskResult
{
    public bool IsPassed { get; }
    public object? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public object? Details { get; }

    private McpTaskResult(bool passed, object? value, string? code, string? message, object? details)
    {
        IsPassed = passed;
        Value = value;
        ErrorCode = code;
        ErrorMessage = message;
        Details = details;
    }

    public static McpTaskResult Pass(object? value = null) => new(true, value, null, null, null);

    public static McpTaskResult Fail(string code, string message, object? details = null)
        => new(false, null, string.IsNullOrWhiteSpace(code) ? "TASK_FAILED" : code, message, details);
}
