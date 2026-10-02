using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Bound effects without turning an already-run write into a retryable rejection.</summary>
public static class ResponseEffectSummary
{
    public static JObject Compact(JObject? data, int originalBytes)
    {
        var compact = new JObject();
        if (data != null)
            foreach (var key in new[] { "ok", "created", "existing", "updated", "deleted", "dry_run", "rebuilt", "completed", "count", "created_count", "updated_count", "deleted_count", "affected_count", "completed_count", "failed_count", "rolled_back", "mutation_applied", "readback_required", "source_unchanged", "document_unchanged", "ui_restored", "model_bom_changed", "name", "document", "sheet", "sheet_id", "path", "format", "error" })
                if (data[key] != null) compact[key] = Bound(data[key]!);
        if (data == null || !new[] { "ok", "created", "updated", "deleted", "dry_run", "existing", "completed", "completed_count", "mutation_applied" }.Any(k => data[k] != null)) compact["outcome_unknown"] = true;
        foreach (var key in new[] { "items", "files" })
            if (data?[key] is JArray items) compact[key] = new JArray(items.OfType<JObject>().Take(100).Select(item => new JObject(item.Properties().Where(p => new[] { "name", "kind", "group", "path", "ok", "created", "updated", "deleted", "rebuilt", "attached", "value", "unit", "bytes", "error" }.Contains(p.Name)).Select(p => new JProperty(p.Name, Bound(p.Value))))));
        compact["response_compacted"] = true;
        compact["original_response_bytes"] = originalBytes;
        compact["size_warning"] = "Write result details omitted. Inspect named objects/files or History for completed effects; do not replay this write to recover detail.";
        return compact;
    }

    private static JToken Bound(JToken token)
    {
        if (token.Type == JTokenType.String) { var text = token.Value<string>() ?? ""; return new JValue(text.Length > 512 ? text.Substring(0, 512) + "…" : text); }
        if (token is JObject obj) return new JObject(obj.Properties().Take(4).Select(p => new JProperty(p.Name, Bound(p.Value))));
        return token.Type == JTokenType.Array ? new JValue("Details omitted; readback required.") : token.DeepClone();
    }
}
