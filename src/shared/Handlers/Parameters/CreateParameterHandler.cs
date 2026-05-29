#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

/// <summary>
/// <c>create_parameter</c> — creates a new user parameter on the active part document via
/// <c>UserParameters.AddByExpression(name, expression, unit)</c>, then updates the document.
/// The unit is passed as a string (e.g. "mm", "deg", "ul" for unitless).
/// </summary>
public sealed class CreateParameterHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_parameter";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        if (activeDoc is not PartDocument doc)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "create_parameter requires an active part document");

        string name = (p["name"]?.Type == JTokenType.String) ? (string)p["name"]! : "";
        string expression = (p["expression"]?.Type == JTokenType.String) ? (string)p["expression"]! : "";
        string unit = (p["unit"]?.Type == JTokenType.String) ? (string)p["unit"]! : "";
        if (string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "name is required");
        if (string.IsNullOrWhiteSpace(expression))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "expression is required");
        if (string.IsNullOrWhiteSpace(unit))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "unit is required (e.g. mm, deg, ul)");

        UserParameter prm;
        try
        {
            prm = doc.ComponentDefinition.Parameters.UserParameters.AddByExpression(name, expression, unit);
            doc.Update();
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to create parameter: " + ex.Message);
        }

        return Ok(ctx, ParameterValueDto.From(prm, includeKind: false));
    }
}
#endif
