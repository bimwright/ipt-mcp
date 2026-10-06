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
        var payloadText = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        try
        {
            if (payloadText != null && JObject.Parse(payloadText).Value<bool?>("ok") == false) result.IsError = true;
        }
        catch (JsonReaderException) { }
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
        if (command == "send_code" || command == "run_baked_tool")
        {
            try
            {
                var writer = new Bimwright.Ipt.Shared.Infrastructure.ResponseSpillWriter(
                    System.IO.Path.Combine(config.DataDirectory, "spill"), config.SpillRetentionHours);
                // Decode the outer MCP JSON before masking: escaped quote sequences in
                // content.text must not conceal credentials from the string masker.
                var structured = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions));
                var serialized = ((JToken)ServerLogger.MaskParams(structured)!).ToString(Formatting.None);
                var path = writer.Write(command + "-response", ".json", serialized);
                return Payload(new JObject {
                    ["ok"] = result.IsError != true,
                    ["mutation_applied"] = JValue.CreateNull(),
                    ["response_file"] = path,
                    ["response_preview"] = Bimwright.Ipt.Shared.Infrastructure.ResponseSpillWriter.Utf8Prefix(serialized, 512),
                    ["spill_schema"] = new JObject { ["version"] = 1, ["format"] = "application/json", ["type"] = "CallToolResult" },
                    ["readback_required"] = true,
                    ["size_warning"] = "Read response_file for details. Do not re-run this operation to recover output."
                }, result.IsError == true);
            }
            catch (System.IO.IOException) { }
            catch (System.UnauthorizedAccessException) { }
        }
        if (readOnly)
            return Payload(new JObject { ["ok"] = false, ["error"] = new JObject {
                ["code"] = "RESPONSE_TOO_LARGE",
                ["message"] = $"Response {originalBytes} UTF-8 bytes exceeds the {limit}-byte final response budget. {Bimwright.Ipt.Shared.Contracts.ResponseSizePolicyCatalog.GetNarrowingHint(command)} Do not repeat the same unscoped request."
            } }, true);

        // A completed write must never be presented as a rejected, replayable operation.
        var compact = Bimwright.Ipt.Shared.Contracts.ResponseEffectSummary.Compact(data, originalBytes);
        if (command == "send_code" || command == "run_baked_tool") compact["mutation_applied"] = JValue.CreateNull();
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
