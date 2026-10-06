using System;
using System.Text;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Size policy for add-in responses (spec F3-a, ported from rvt-mcp). Measured on the compact
/// payload at the add-in: warning from 64 KiB, strong warning above 256 KiB, reject above
/// 700 KiB with <c>RESPONSE_TOO_LARGE</c> plus a per-command narrowing hint. The reject budget
/// sits below the 5 MB transport fence (<see cref="Check"/>) so the agent's context window is
/// protected before the wire limit is ever reached.
/// </summary>
public static class ResponseSizeGuard
{
    public const int WarningBytes = 64 * 1024;
    public const int StrongWarningBytes = 256 * 1024;
    public const int RejectBytes = 700 * 1024;

    public sealed class Decision
    {
        public int ByteCount { get; set; }
        public string? WarningLevel { get; set; }     // "warning" | "strong_warning" | null
        public string? AgentWarning { get; set; }     // attached to the response as size_warning
        public bool Reject { get; set; }
        public string? RejectError { get; set; }
    }

    /// <summary>Final transport fence — callers configure the byte limit (5 MB in production).</summary>
    public static bool Check(string serialized, int maxBytes, out InventorError? error)
    {
        var size = System.Text.Encoding.UTF8.GetByteCount(serialized);
        if (size <= maxBytes) { error = null; return true; }
        error = new InventorError
        {
            Code = "RESPONSE_TOO_LARGE",
            Message = $"Response {size} bytes exceeds the configured limit of {maxBytes} bytes. " +
                      "Narrow the query (max_items/max_depth) and retry."
        };
        return false;
    }

    /// <summary>Three-tier evaluation on the compact payload for one command.</summary>
    public static Decision Evaluate(string? commandName, string? serializedPayload)
    {
        var byteCount = Encoding.UTF8.GetByteCount(serializedPayload ?? string.Empty);
        var hint = ResponseSizePolicyCatalog.GetNarrowingHint(commandName);

        if (byteCount > RejectBytes)
        {
            return new Decision
            {
                ByteCount = byteCount,
                Reject = true,
                RejectError =
                    $"Response {byteCount} bytes exceeds the {RejectBytes}-byte response budget for command={commandName}. " +
                    $"{hint} Do not retry the same unscoped request."
            };
        }

        if (byteCount >= WarningBytes)
        {
            var strong = byteCount > StrongWarningBytes;
            return new Decision
            {
                ByteCount = byteCount,
                WarningLevel = strong ? "strong_warning" : "warning",
                AgentWarning = strong
                    ? $"Oversized response strong warning: {byteCount} bytes for command={commandName}. {hint}"
                    : $"Oversized response warning: {byteCount} bytes for command={commandName}. Consider narrowing the next request. {hint}"
            };
        }

        return new Decision { ByteCount = byteCount };
    }
}
