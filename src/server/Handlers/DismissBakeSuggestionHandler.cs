using System;
using Bimwright.Inventor.Server.Bake;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Handlers;

public static class DismissBakeSuggestionHandler
{
    public static string Handle(BakeDb db, string id, string action)
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        var suggestion = db.GetSuggestion(id);
        if (suggestion == null)
        {
            return Fail("not_found", "Bake suggestion was not found.");
        }

        if (action != "snooze_30d" && action != "never" && action != "never_with_gap_signal")
        {
            return Fail("invalid_action", "action must be snooze_30d, never, or never_with_gap_signal.");
        }

        db.TryUpdateSuggestionState(id, BakeSuggestionStates.Dismissed);
        return new JObject { ["ok"] = true, ["id"] = id, ["state"] = BakeSuggestionStates.Dismissed }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string Fail(string code, string message)
        => new JObject { ["ok"] = false, ["error_code"] = code, ["message"] = message }.ToString(Newtonsoft.Json.Formatting.None);
}
