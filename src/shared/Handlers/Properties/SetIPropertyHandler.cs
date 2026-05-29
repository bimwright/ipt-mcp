#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Properties;

/// <summary>
/// <c>set_iproperty</c> — sets an iProperty value on the active document by property-set name and
/// property name. The value is supplied as a string and assigned to the property.
/// </summary>
public sealed class SetIPropertyHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_iproperty";
    public bool IsReadOnly => false;

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
        if (p["value"] is null || p["value"]!.Type == JTokenType.Null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "value is required");
        string value = p["value"]!.ToString();

        PropertySet? set = PropertyAccess.FindSet(doc, setName);
        if (set is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "property set '" + setName + "' not found");

        Property? prop = PropertyAccess.FindProperty(set, propName);
        if (prop is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "property '" + propName + "' not found in set '" + setName + "'");

        try
        {
            prop.Value = value;
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to set iProperty: " + ex.Message);
        }

        object? written = null;
        try { written = prop.Value; } catch { /* unreadable */ }

        return Ok(ctx, new JObject
        {
            ["set_name"] = set.Name,
            ["prop_name"] = prop.Name,
            ["value"] = written is null ? JValue.CreateNull() : JToken.FromObject(written.ToString() ?? ""),
        });
    }
}
#endif
