using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Guard drawing responses after serialization, preserving completed write effects.</summary>
public static class DrawingResponsePolicy
{
    /// <summary>Keep partial effects while making failed drawing outcomes visible to journals/history.</summary>
    public static void NormalizeOutcome(InventorCommandResult result)
    {
        if (result.Data is not JObject data || data.Value<bool?>("ok") != false) return;
        result.Ok = false;
        result.Error = new InventorError
        {
            Code = data["error"] is JObject details ? (string?)details["code"] ?? InventorErrorCodes.API_ERROR : InventorErrorCodes.API_ERROR,
            Message = data["error"] is JObject error ? (string?)error["message"] ?? "Drawing operation failed."
                : (string?)data["error"] ?? "Drawing operation failed."
        };
    }
    public static JObject Apply(string command, JObject data, int budget = ResponseSizeGuard.RejectBytes)
    {
        var serialized = data.ToString(Formatting.None); var bytes = Encoding.UTF8.GetByteCount(serialized);
        if (bytes <= budget)
        {
            var decision = ResponseSizeGuard.Evaluate(command, serialized); if (decision.AgentWarning != null) data["size_warning"] = decision.AgentWarning; return data;
        }
        if (command == "get_drawing_info") return new JObject { ["ok"] = false, ["error"] = new JObject { ["code"] = InventorErrorCodes.RESPONSE_TOO_LARGE, ["message"] = "Drawing query exceeds the response budget. Use one sheet, include=summary, smaller max_items or offset. Do not repeat the same query." } };
        var compact = new JObject();
        foreach (var key in new[] { "ok", "created", "existing", "updated", "completed", "count", "created_count", "completed_count", "failed_count", "rolled_back", "mutation_applied", "readback_required", "source_unchanged", "document_unchanged", "ui_restored", "model_bom_changed" }) if (data[key] != null) compact[key] = data[key]!.DeepClone();
        foreach (var key in new[] { "name", "document", "path", "format" }) if (data[key] != null) compact[key] = Bound(data[key]!);
        if (data["items"] is JArray items) compact["items"] = new JArray(items.OfType<JObject>().Take(100).Select(item => new JObject(item.Properties().Where(p => new[] { "name", "created", "attached", "value", "unit" }.Contains(p.Name)).Select(p => new JProperty(p.Name, Bound(p.Value))))));
        if (data["files"] is JArray files) compact["files"] = new JArray(files.OfType<JObject>().Take(1000).Select(file => new JObject(file.Properties().Where(p => new[] { "ok", "status", "path", "bytes", "error" }.Contains(p.Name)).Select(p => new JProperty(p.Name, Bound(p.Value))))));
        if (data["error"] != null) compact["error"] = Bound(data["error"]!);
        compact["response_compacted"] = true; compact["original_response_bytes"] = bytes; compact["size_warning"] = "Completed effects are summarized. Inspect named items/files; do not replay this write to recover omitted detail.";
        // Absolute transport guard still bounds pathological names/paths; aggregate effects
        // remain truthful even if every individual locator cannot fit the configured budget.
        if (Encoding.UTF8.GetByteCount(compact.ToString(Formatting.None)) > budget) { compact.Remove("items"); compact.Remove("files"); compact["locators_omitted"] = true; }
        return compact;
    }
    private static JToken Bound(JToken token)
    {
        if (token.Type == JTokenType.String) { var text = token.Value<string>() ?? ""; return text.Length > 512 ? new JValue(text.Substring(0, 512) + "…") : token.DeepClone(); }
        if (token is JObject error) return new JObject(error.Properties().Take(4).Select(p => new JProperty(p.Name, Bound(p.Value)))); return token.DeepClone();
    }
}
