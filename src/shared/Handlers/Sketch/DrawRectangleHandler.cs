#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// <c>draw_rectangle</c> — add a two-point (corner-to-corner) rectangle. The Inventor API exposes
/// this as <c>SketchLines.AddAsTwoPointRectangle</c>, which returns the four created lines as a
/// <c>SketchEntitiesEnumerator</c>. Returns the ids of the four new line entities.
/// </summary>
public sealed class DrawRectangleHandler : HandlerBase, IInventorCommand
{
    public string Name => "draw_rectangle";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "draw_rectangle", out var app, out var part, out var failure))
            return failure!;

        if (p["x1"] is null || p["y1"] is null || p["x2"] is null || p["y2"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "x1,y1,x2,y2 (mm) are required");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var before = sketch.SketchEntities.Count;
            var c1 = SketchSupport.Pt(app, p.Value<double>("x1"), p.Value<double>("y1"));
            var c2 = SketchSupport.Pt(app, p.Value<double>("x2"), p.Value<double>("y2"));
            sketch.SketchLines.AddAsTwoPointRectangle(c1, c2);
            var after = sketch.SketchEntities.Count;

            var ids = new JArray();
            for (var i = before + 1; i <= after; i++) ids.Add(i.ToString());
            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["entity_ids"] = ids,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
