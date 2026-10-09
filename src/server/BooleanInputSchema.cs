using System.Text.Json;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server;

/// <summary>Compatibility experiment: change only true-valued schema positions, never JSON data.</summary>
internal static class BooleanInputSchema
{
    internal static JsonElement Normalize(JsonElement input)
    {
        var root = JToken.Parse(input.GetRawText());
        Visit(root);
        using var document = JsonDocument.Parse(root.ToString());
        return document.RootElement.Clone();
    }

    private static void Visit(JToken node)
    {
        if (node.Type == JTokenType.Boolean && node.Value<bool>())
        {
            node.Replace(new JObject());
            return;
        }
        if (node is not JObject obj) return;
        foreach (var key in new[] { "properties", "patternProperties", "$defs", "definitions", "dependentSchemas" })
            if (obj[key] is JObject map)
                foreach (var property in map.Properties()) Visit(property.Value);
        foreach (var key in new[] { "items", "contains", "propertyNames", "not", "if", "then", "else" })
            if (obj[key] is { } child)
            {
                if (child is JArray tuple) { foreach (var item in tuple.ToArray()) Visit(item); }
                else Visit(child);
            }
        foreach (var key in new[] { "anyOf", "oneOf", "allOf", "prefixItems" })
            if (obj[key] is JArray branches)
                foreach (var branch in branches.ToArray()) Visit(branch);
        // Openness flags are policies, not the bare property schemas under comparison.
        foreach (var key in new[] { "additionalProperties", "unevaluatedProperties", "unevaluatedItems", "additionalItems" })
            if (obj[key] is JObject schema) Visit(schema);
    }
}
