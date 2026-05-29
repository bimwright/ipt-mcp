using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Shared.ToolBaker;

/// <summary>
/// Builds a validated <see cref="BakedToolRecord"/> from an <c>apply_bake</c> request envelope.
/// API-agnostic; ported from nwd-mcp.
/// </summary>
public static class BakedToolRuntimeCommandFactory
{
    public static BakedToolRecord FromApplyRequest(JObject request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var name = (string?)request["tool_name"] ?? "";
        var source = (string?)request["source"] ?? "preset";
        var handlerTool = (string?)request["handler_tool"] ?? "";
        var fixedArgs = request["fixed_args"] as JObject ?? new JObject();
        var sequence = request["sequence"] as JArray ?? new JArray();
        var sourceCode = (string?)request["source_code"] ?? "";
        var record = new BakedToolRecord
        {
            Name = name,
            Description = (string?)request["description"] ?? name,
            Source = source,
            ParamsSchema = CompactJson(request["params_schema"]) ?? "{}",
            CompatMap = "{}",
            SourceCode = sourceCode,
            HandlerTool = handlerTool,
            FixedArgs = fixedArgs.ToString(Formatting.None),
            Sequence = sequence.ToString(Formatting.None),
            CreatedFromSuggestionId = (string?)request["created_from_suggestion_id"],
            ReviewedByUser = true,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o")
        };
        var validation = ToolCompiler.ValidateRecord(record);
        if (!validation.Ok)
        {
            throw new InvalidOperationException(validation.Error);
        }

        return record;
    }

    private static string? CompactJson(JToken? token)
    {
        if (token == null)
        {
            return null;
        }

        if (token.Type == JTokenType.String)
        {
            return token.Value<string>();
        }

        return token.ToString(Formatting.None);
    }
}
