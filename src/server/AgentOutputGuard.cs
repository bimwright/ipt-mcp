using System;
using System.Linq;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>One policy at the MCP exit, including server-only and validation results.</summary>
internal static class AgentOutputGuard
{
    internal static int Measure(CallToolResult result)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions).Length;

    internal static CallToolResult Apply(string toolName, CallToolResult result, InventorMcpConfig config)
    {
        var command = toolName.StartsWith("inventor_", StringComparison.Ordinal) ? toolName.Substring(9) : toolName;
        var originalBytes = Measure(result);
        var limit = config.EnableOutputGuard ? Math.Min(config.OutputBudgetBytes, config.MaxResponseBytes) : config.MaxResponseBytes;
        if (originalBytes <= limit && config.EnableOutputGuard && originalBytes >= config.OutputWarningBytes)
        {
            var strong = originalBytes > config.OutputStrongWarningBytes;
            result.Content = result.Content.Concat(new[] { new TextContentBlock { Text = $"Response size {(strong ? "strong warning" : "warning")}: {originalBytes} UTF-8 bytes. {Bimwright.Ipt.Shared.Contracts.ResponseSizePolicyCatalog.GetNarrowingHint(command)}" } }).ToList();
        }
        if (Measure(result) <= limit) return result;

        var readOnly = ToolCatalog.IsReadOnly(command);
        JObject? data = null;
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        try { if (text != null) data = JObject.Parse(text); } catch (JsonReaderException) { }
        if (readOnly)
            return Payload(new JObject { ["ok"] = false, ["error"] = new JObject {
                ["code"] = "RESPONSE_TOO_LARGE",
                ["message"] = $"Response {originalBytes} UTF-8 bytes exceeds the {limit}-byte final response budget. {Bimwright.Ipt.Shared.Contracts.ResponseSizePolicyCatalog.GetNarrowingHint(command)} Do not repeat the same unscoped request."
            } }, true);

        // A completed write must never be presented as a rejected, replayable operation.
        var compact = Bimwright.Ipt.Shared.Contracts.ResponseEffectSummary.Compact(data, originalBytes);
        var summary = Payload(compact, result.IsError == true || data?.Value<bool?>("ok") == false);
        if (Measure(summary) <= limit) return summary;
        compact.Remove("items"); compact.Remove("files"); compact["locators_omitted"] = true;
        summary = Payload(compact, summary.IsError == true);
        if (Measure(summary) <= limit) return summary;
        // Pathological error objects/locators cannot bypass the independent transport fence.
        return Payload(new JObject { ["outcome_unknown"] = true, ["response_compacted"] = true,
            ["original_response_bytes"] = originalBytes, ["size_warning"] = "Write outcome needs readback. Do not replay this write." }, result.IsError == true);
    }

    private static CallToolResult Payload(JObject data, bool error) => new() {
        IsError = error, Content = new[] { new TextContentBlock { Text = data.ToString(Formatting.None) } }
    };
}
