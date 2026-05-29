#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Sketch;

/// <summary>
/// <c>project_geometry</c> — project model edges (by edge id) into the target sketch via
/// <c>PlanarSketch.AddByProjectingEntity</c>. Returns how many curves were projected.
/// </summary>
public sealed class ProjectGeometryHandler : HandlerBase, IInventorCommand
{
    public string Name => "project_geometry";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "project_geometry requires an active part document");

        if (p["edge_ids"] is not JArray edgeIds || edgeIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edge_ids[] is required and must be non-empty");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            sketch.Edit();
            try
            {
                var projected = 0;
                foreach (var token in edgeIds)
                {
                    var edge = EntityResolver.ResolveEdge(def, token.ToString());
                    sketch.AddByProjectingEntity(edge);
                    projected++;
                }
                return Ok(ctx, new JObject
                {
                    ["sketch_name"] = sketch.Name,
                    ["projected_count"] = projected,
                });
            }
            finally { sketch.ExitEdit(); }
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
