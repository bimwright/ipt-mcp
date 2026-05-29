#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Sketch;

/// <summary>
/// <c>draw_line</c> — add a two-point line to the target sketch (mm in, cm to the API). Returns the
/// 1-based index of the new entity within the sketch so it can be referenced by later tools.
/// </summary>
public sealed class DrawLineHandler : HandlerBase, IInventorCommand
{
    public string Name => "draw_line";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "draw_line requires an active part document");

        if (p["x1"] is null || p["y1"] is null || p["x2"] is null || p["y2"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "x1,y1,x2,y2 (mm) are required");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var start = SketchSupport.Pt(app, p.Value<double>("x1"), p.Value<double>("y1"));
            var end = SketchSupport.Pt(app, p.Value<double>("x2"), p.Value<double>("y2"));
            sketch.SketchLines.AddByTwoPoints(start, end);
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
