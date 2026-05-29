#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Handlers;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Document;

/// <summary>
/// <c>set_material</c> — assigns a material (by name) to the active part document. The material must
/// already exist as a material asset in the document. Returns <c>WRONG_DOCUMENT_TYPE</c> if the active
/// document is not a part, <c>INVALID_ARGUMENT</c> if the named material is not found.
/// </summary>
public sealed class SetMaterialHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_material";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        if (activeDoc is not PartDocument doc)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "set_material requires an active part document");

        string name = (p["material_name"]?.Type == JTokenType.String) ? (string)p["material_name"]! : "";
        if (string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "material_name is required");

        Material? material = null;
        try
        {
            foreach (Material m in doc.Materials)
            {
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    material = m;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to enumerate materials: " + ex.Message);
        }

        if (material is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "material '" + name + "' not found in the document's material library");

        try
        {
            doc.ComponentDefinition.Material = material;
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to assign material: " + ex.Message);
        }

        return Ok(ctx, new JObject { ["material_name"] = material.Name });
    }
}
#endif
