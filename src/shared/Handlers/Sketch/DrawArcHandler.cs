#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// <c>draw_arc</c> — add a center-point arc by start angle + sweep. The MCP surface gives start/end
/// angles in degrees CCW; Inventor's <c>SketchArcs.AddByCenterStartSweepAngle(center, radius,
/// startAngle, sweepAngle)</c> wants radians and a <b>sweep</b> (end-start), so we convert and derive
/// the sweep here.
/// </summary>
public sealed class DrawArcHandler : HandlerBase, IInventorCommand
{
    public string Name => "draw_arc";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "draw_arc", out var app, out var part, out var failure))
            return failure!;

        if (p["cx"] is null || p["cy"] is null || p["radius"] is null || p["start_deg"] is null || p["end_deg"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "cx,cy,radius,start_deg,end_deg are required");
        var radiusMm = p.Value<double>("radius");
        if (radiusMm <= 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "radius must be greater than 0");
        var startDeg = p.Value<double>("start_deg");
        var endDeg = p.Value<double>("end_deg");
        var sweepDeg = endDeg - startDeg;
        if (Math.Abs(sweepDeg) < 1e-9)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "start_deg and end_deg must differ");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var center = SketchSupport.Pt(app, p.Value<double>("cx"), p.Value<double>("cy"));
            sketch.SketchArcs.AddByCenterStartSweepAngle(
                center,
                UnitConvert.MmToCm(radiusMm),
                UnitConvert.DegToRad(startDeg),
                UnitConvert.DegToRad(sweepDeg));
            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["entity_id"] = sketch.SketchEntities.Count.ToString(),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
