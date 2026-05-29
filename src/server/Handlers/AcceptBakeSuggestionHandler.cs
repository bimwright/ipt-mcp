using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bimwright.Ipt.Server.Bake;
using Bimwright.Ipt.Shared.ToolBaker;
using Bimwright.Ipt.Shared.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Handlers;

/// <summary>
/// Accepts a bake suggestion: validates the desired name + output choice + collision, builds an
/// <c>apply_bake</c> request, applies it through the add-in, redacts secrets, and persists the
/// resulting <see cref="BakedToolRecord"/> in the server registry. Ported from nwd-mcp.
/// </summary>
public static class AcceptBakeSuggestionHandler
{
    private static readonly Regex ToolNamePattern = new Regex("^[a-z][a-z0-9_]{2,63}$", RegexOptions.Compiled);

    public static async Task<string> HandleAsync(
        BakeDb db,
        string id,
        string name,
        string outputChoice = "mcp_only",
        string? paramsSchema = null,
        Func<JObject, Task<JObject>>? pluginApply = null)
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        var suggestion = db.GetSuggestion(id);
        if (suggestion == null) return Failure("not_found", "Bake suggestion was not found.");
        if (string.IsNullOrEmpty(name) || !ToolNamePattern.IsMatch(name)) return Failure("invalid_name", "Tool name must use snake_case and start with a letter.");
        if (!string.Equals(outputChoice ?? "mcp_only", "mcp_only", StringComparison.Ordinal))
        {
            return Failure("unsupported_output_choice", "V1 baked tools support output_choice=mcp_only.");
        }
        if (db.ReadRegistryRecords().Any(r => string.Equals(r.Name, name, StringComparison.Ordinal))) return Failure("duplicate_tool_name", "A baked tool with this name already exists.");
        if (pluginApply == null) return Failure("plugin_apply_unavailable", "Plugin apply_bake transport is not configured.");

        var payload = ParsePayload(suggestion.PayloadJson);
        JObject request;
        try
        {
            request = BuildApplyRequest(suggestion, payload, name, outputChoice, paramsSchema);
        }
        catch (JsonException ex)
        {
            return Failure("invalid_params_schema", ex.Message);
        }
        JObject applyResult;
        try
        {
            applyResult = await pluginApply(request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Failure("plugin_apply_failed", ex.Message);
        }

        if (applyResult == null || applyResult.Value<bool?>("success") != true)
        {
            return Failure((string?)applyResult?["error_code"] ?? "plugin_apply_failed", (string?)applyResult?["message"] ?? "Plugin apply_bake failed.");
        }

        var record = BakedToolRuntimeCommandFactory.FromApplyRequest(request);
        record.Description = (string?)applyResult["description"] ?? record.Description;
        record.ParamsSchema = (string?)applyResult["params_schema"] ?? record.ParamsSchema;
        record.SourceCode = BakeRedactor.RedactSource((string?)applyResult["source_code"] ?? record.SourceCode);
        record.CreatedFromSuggestionId = suggestion.Id;
        if (!db.TryInsertRegistryRecord(record))
        {
            return Failure("registry_insert_failed", "A baked tool with this name already exists.");
        }

        db.TryUpdateSuggestionState(suggestion.Id, BakeSuggestionStates.Accepted);
        return new JObject
        {
            ["ok"] = true,
            ["tool_name"] = record.Name,
            ["state"] = BakeSuggestionStates.Accepted
        }.ToString(Formatting.None);
    }

    private static JObject BuildApplyRequest(BakeSuggestionRecord suggestion, JObject payload, string name, string? outputChoice, string? paramsSchema)
    {
        var source = suggestion.Source ?? "preset";
        var handlerTool = (string?)payload["tool"] ?? string.Empty;
        var sequence = payload["sequence"] as JArray;
        var schema = ResolveSchema(paramsSchema, payload);
        var fixedArgs = payload["fixed_args"] as JObject ?? BuildDefaults(payload);

        return new JObject
        {
            ["suggestion_id"] = suggestion.Id,
            ["tool_name"] = name,
            ["description"] = suggestion.Description ?? suggestion.Title ?? name,
            ["source"] = source,
            ["output_choice"] = outputChoice ?? "mcp_only",
            ["params_schema"] = schema,
            ["handler_tool"] = handlerTool,
            ["fixed_args"] = fixedArgs,
            ["sequence"] = sequence ?? new JArray(handlerTool),
            ["source_code"] = source == "macro"
                ? BakedToolRuntimeSource.BuildMacro((sequence ?? new JArray(handlerTool)).Values<string>().OfType<string>().ToArray())
                : BakedToolRuntimeSource.BuildPreset(handlerTool, fixedArgs),
            ["created_from_suggestion_id"] = suggestion.Id
        };
    }

    private static string ResolveSchema(string? overrideSchema, JObject payload)
    {
        if (!string.IsNullOrWhiteSpace(overrideSchema))
        {
            return JObject.Parse(overrideSchema).ToString(Formatting.None);
        }

        var properties = new JObject();
        var kinds = payload["sample"]?["parameter_kinds"] as JObject;
        if (kinds != null)
        {
            foreach (var property in kinds.Properties())
            {
                properties[property.Name] = new JObject { ["type"] = ToJsonType((string?)property.Value ?? "") };
            }
        }

        return new JObject { ["type"] = "object", ["properties"] = properties }.ToString(Formatting.None);
    }

    private static JObject BuildDefaults(JObject payload)
    {
        var defaults = new JObject();
        var kinds = payload["sample"]?["parameter_kinds"] as JObject;
        if (kinds == null) return defaults;
        foreach (var property in kinds.Properties())
        {
            defaults[property.Name] = DefaultForKind((string?)property.Value ?? "");
        }
        return defaults;
    }

    private static JToken DefaultForKind(string kind)
    {
        switch (kind)
        {
            case "number": return 0;
            case "bool": return false;
            case "array": return new JArray();
            case "object": return new JObject();
            default: return string.Empty;
        }
    }

    private static string ToJsonType(string kind)
    {
        switch (kind)
        {
            case "number": return "number";
            case "bool": return "boolean";
            case "array": return "array";
            case "object": return "object";
            default: return "string";
        }
    }

    private static JObject ParsePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try { return JObject.Parse(json); }
        catch (JsonException) { return new JObject(); }
    }

    private static string Failure(string code, string message)
    {
        return new JObject
        {
            ["ok"] = false,
            ["error_code"] = code,
            ["message"] = message
        }.ToString(Formatting.None);
    }
}
