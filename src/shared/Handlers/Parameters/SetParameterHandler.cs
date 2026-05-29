#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Parameters;

/// <summary>
/// <c>set_parameter</c> — sets an existing parameter's expression by name (e.g. "25 mm" or a numeric
/// expression), then updates the document. Requires a part document.
/// </summary>
public sealed class SetParameterHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_parameter";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        if (activeDoc is not PartDocument doc)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "set_parameter requires an active part document");

        string name = (p["name"]?.Type == JTokenType.String) ? (string)p["name"]! : "";
        if (string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "name is required");
        if (p["value"] is null || p["value"]!.Type == JTokenType.Null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "value is required");
        string value = p["value"]!.ToString();

        Parameter? prm = GetParameterHandler.FindParameter(doc, name);
        if (prm is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "parameter '" + name + "' not found");

        try
        {
            prm.Expression = value;
            doc.Update();
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to set parameter: " + ex.Message);
        }

        object? evaluated = null;
        try { evaluated = prm.Value; } catch { /* no numeric value */ }

        return Ok(ctx, new JObject
        {
            ["name"] = prm.Name,
            ["expression"] = prm.Expression,
            ["value"] = evaluated is double d ? (JToken)d : JValue.CreateNull(),
            ["unit"] = prm.get_Units(),
        });
    }
}
#endif
