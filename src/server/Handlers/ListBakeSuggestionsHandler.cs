using System;
using System.Linq;
using Bimwright.Inventor.Server.Bake;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Handlers;

public static class ListBakeSuggestionsHandler
{
    public static string Handle(BakeDb db)
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        var suggestions = db.ListSuggestions()
            .Where(s => !string.Equals(s.State, BakeSuggestionStates.Archived, StringComparison.Ordinal))
            .Select(s => new JObject
            {
                ["id"] = s.Id,
                ["title"] = s.Title,
                ["source"] = s.Source,
                ["score"] = s.Score,
                ["state"] = s.State,
                ["created_at"] = s.CreatedAt,
                ["output_choices"] = ParsePayload(s.PayloadJson)["output_choices"] ?? new JArray("mcp_only")
            });

        return new JObject { ["suggestions"] = new JArray(suggestions) }.ToString(Formatting.None);
    }

    private static JObject ParsePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try { return JObject.Parse(json); }
        catch (JsonException) { return new JObject(); }
    }
}
