#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>create_work_point</c> — create a fixed work point at a position (spec F4-P3).
/// <c>position</c> accepts {x,y,z} or [x,y,z] in mm; <c>construction</c>/<c>name</c>/<c>visible</c>
/// optional. Reference-driven variants (by point/planes/lines) are deferred — no run evidence.
/// </summary>
public sealed class CreateWorkPointHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_work_point";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "create_work_point", out var app, out var part, out var failure))
            return failure!;

        if (!Bimwright.Ipt.Shared.Handlers.Vec3Params.TryParse(p["position"], "position",
                out var x, out var y, out var z, out var posErr))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, posErr);

        try
        {
            var def = part.ComponentDefinition;
            var construction = p["construction"]?.Value<bool>() ?? false;
            var pt = app.TransientGeometry.CreatePoint(
                UnitConvert.MmToCm(x), UnitConvert.MmToCm(y), UnitConvert.MmToCm(z));
            var wp = def.WorkPoints.AddFixed(pt, construction);

            var name = (string?)p["name"];
            if (!string.IsNullOrWhiteSpace(name)) wp.Name = name;
            if (p["visible"] is { } vis) wp.Visible = vis.Value<bool>();

            var data = new JObject
            {
                ["work_point_name"] = wp.Name,
                ["position_mm"] = new JArray(x, y, z),
                ["construction"] = construction,
            };
            // Inventor silently refuses to name construction work points — surface that honestly.
            if (!string.IsNullOrWhiteSpace(name))
                data["name_applied"] = string.Equals(wp.Name, name, StringComparison.Ordinal);
            return Ok(ctx, data);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
