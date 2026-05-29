#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

/// <summary>
/// <c>get_parameter</c> — read-only. Returns a single parameter of the active part document by name:
/// expression, evaluated value, and unit.
/// </summary>
public sealed class GetParameterHandler : HandlerBase, IInventorCommand
{
    public string Name => "get_parameter";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? activeDoc;
        try { activeDoc = app.ActiveDocument; } catch { activeDoc = null; }
        if (activeDoc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        if (activeDoc is not PartDocument doc)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "get_parameter requires an active part document");

        string name = (p["name"]?.Type == JTokenType.String) ? (string)p["name"]! : "";
        if (string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "name is required");

        Parameter? prm = FindParameter(doc, name);
        if (prm is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "parameter '" + name + "' not found");

        return Ok(ctx, ParameterValueDto.From(prm));
    }

    internal static Parameter? FindParameter(PartDocument doc, string name)
    {
        foreach (Parameter prm in doc.ComponentDefinition.Parameters)
        {
            if (string.Equals(prm.Name, name, StringComparison.Ordinal))
                return prm;
        }
        return null;
    }
}
#endif
