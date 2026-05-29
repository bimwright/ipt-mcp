#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

/// <summary>
/// <c>list_parameters</c> — read-only. Lists every parameter of the active part document (model + user
/// + reference) with name, expression, evaluated value, and unit. Requires a part document.
/// </summary>
public sealed class ListParametersHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_parameters";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        if (activeDoc is not PartDocument doc)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "list_parameters requires an active part document");

        var arr = new JArray();
        try
        {
            foreach (Parameter prm in doc.ComponentDefinition.Parameters)
            {
                arr.Add(ParameterValueDto.From(prm));
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to read parameters: " + ex.Message);
        }

        return Ok(ctx, new JObject { ["count"] = arr.Count, ["parameters"] = arr });
    }
}
#endif
