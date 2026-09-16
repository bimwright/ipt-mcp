#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Properties;

/// <summary>
/// <c>list_iproperty_sets</c> — read-only. Enumerates the active document's iProperty sets with
/// each set's name, internal name and property names; <c>include_values</c> adds each property's
/// current value as a string (capped at 200 chars). Discovery companion for get/set_iproperty.
/// </summary>
public sealed class ListIPropertySetsHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_iproperty_sets";
    public bool IsReadOnly => true;

    private const int MaxValueChars = 200;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        var includeValues = p["include_values"]?.Value<bool>() ?? false;

        var sets = new JArray();
        foreach (PropertySet set in doc.PropertySets)
        {
            var props = new JArray();
            foreach (Property prop in set)
            {
                var pj = new JObject { ["name"] = prop.Name };
                string? displayName = null;
                try { displayName = prop.DisplayName; } catch { }
                if (!string.IsNullOrEmpty(displayName) &&
                    !string.Equals(displayName, prop.Name, StringComparison.Ordinal))
                    pj["display_name"] = displayName;
                if (includeValues)
                {
                    string? value = null;
                    try { value = prop.Value?.ToString(); } catch { }
                    if (value is not null && value.Length > MaxValueChars)
                        value = value.Substring(0, MaxValueChars) + "…";
                    pj["value"] = value is null ? JValue.CreateNull() : JToken.FromObject(value);
                }
                props.Add(pj);
            }

            string? internalName = null;
            try { internalName = set.InternalName; } catch { }
            var sj = new JObject
            {
                ["name"] = set.Name,
                ["count"] = props.Count,
                ["properties"] = props,
            };
            if (!string.IsNullOrEmpty(internalName)) sj["internal_name"] = internalName;
            sets.Add(sj);
        }

        return Ok(ctx, new JObject { ["sets"] = sets, ["total"] = sets.Count });
    }
}
#endif
