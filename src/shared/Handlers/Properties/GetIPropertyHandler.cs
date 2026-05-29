#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Properties;

/// <summary>
/// <c>get_iproperty</c> — read-only. Returns an iProperty value from the active document by
/// property-set name (e.g. "Design Tracking Properties") and property name (e.g. "Part Number").
/// </summary>
public sealed class GetIPropertyHandler : HandlerBase, IInventorCommand
{
    public string Name => "get_iproperty";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        string setName = (p["set_name"]?.Type == JTokenType.String) ? (string)p["set_name"]! : "";
        string propName = (p["prop_name"]?.Type == JTokenType.String) ? (string)p["prop_name"]! : "";
        if (string.IsNullOrWhiteSpace(setName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "set_name is required");
        if (string.IsNullOrWhiteSpace(propName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "prop_name is required");

        PropertySet? set = PropertyAccess.FindSet(doc, setName);
        if (set is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "property set '" + setName + "' not found");

        Property? prop = PropertyAccess.FindProperty(set, propName);
        if (prop is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "property '" + propName + "' not found in set '" + setName + "'");

        object? value = null;
        try { value = prop.Value; } catch { /* unreadable */ }

        return Ok(ctx, new JObject
        {
            ["set_name"] = set.Name,
            ["prop_name"] = prop.Name,
            ["value"] = value is null ? JValue.CreateNull() : JToken.FromObject(value.ToString() ?? ""),
        });
    }
}
#endif
