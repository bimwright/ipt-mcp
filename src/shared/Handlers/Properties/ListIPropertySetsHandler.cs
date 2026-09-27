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
/// <c>max_items</c> (default 200) caps the total properties emitted across all sets — oversized
/// sets report <c>properties_omitted</c> and the response carries <c>truncated</c>, so a large
/// custom-property set can't hit RESPONSE_TOO_LARGE with no way to narrow the query.
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
        doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var targetFailure);
        if (targetFailure != null) return targetFailure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        var includeValues = p["include_values"]?.Value<bool>() ?? false;
        int maxItems;
        try { maxItems = (int?)p["max_items"] ?? 200; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_items must be an integer"); }
        if (maxItems <= 0) maxItems = 200;

        var sets = new JArray();
        var propertiesTotal = 0;
        var emitted = 0;
        foreach (PropertySet set in doc.PropertySets)
        {
            var props = new JArray();
            var omitted = 0;
            foreach (Property prop in set)
            {
                propertiesTotal++;
                if (emitted >= maxItems) { omitted++; continue; }
                emitted++;

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
                ["count"] = props.Count + omitted,
                ["properties"] = props,
            };
            if (omitted > 0) sj["properties_omitted"] = omitted;
            if (!string.IsNullOrEmpty(internalName)) sj["internal_name"] = internalName;
            sets.Add(sj);
        }

        return Ok(ctx, new JObject
        {
            ["sets"] = sets,
            ["total"] = sets.Count,
            ["properties_total"] = propertiesTotal,
            ["truncated"] = emitted < propertiesTotal,
        });
    }
}
#endif
