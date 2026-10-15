#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

public sealed class ListSketchPointsHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_sketch_points";
    public bool IsReadOnly => true;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, Name, out _, out var part, out var failure)) return failure!;
        try
        {
            if (p["sketch_name"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)p["sketch_name"])) throw new ArgumentException("sketch_name is required");
            var sketch = SketchSupport.ResolveTargetSketch(part.ComponentDefinition, (string)p["sketch_name"]!);
            if (p["max_items"] != null && p["max_items"]!.Type != JTokenType.Integer) throw new ArgumentException("max_items must be an integer");
            var rawLimit = p["max_items"];
            if (rawLimit != null && (!int.TryParse(rawLimit.ToString(), out var parsedLimit) || parsedLimit < 1 || parsedLimit > 1000))
                throw new ArgumentException("max_items must be between 1 and 1000");
            var limit = (int?)rawLimit ?? 200;
            var points = new JArray(); var lookup = new Dictionary<string, JObject>(StringComparer.Ordinal);
            for (var i = 1; i <= Math.Min(limit, sketch.SketchPoints.Count); i++)
            {
                var point = sketch.SketchPoints[i]; var key = SketchPointReferences.Key(point);
                var item = new JObject { ["point_ref"] = SketchPointReferences.Token(part, sketch, key),
                    ["position_mm"] = new JArray(point.Geometry.X * 10, point.Geometry.Y * 10), ["roles"] = new JArray() };
                points.Add(item); lookup[key] = item;
            }
            void Role(SketchPoint point, int index, string role)
            {
                if (lookup.TryGetValue(SketchPointReferences.Key(point), out var item))
                    ((JArray)item["roles"]!).Add(new JObject { ["entity_id"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture), ["role"] = role });
            }
            for (var i = 1; i <= sketch.SketchEntities.Count; i++)
            {
                switch (sketch.SketchEntities[i])
                {
                    case SketchLine line: Role(line.StartSketchPoint, i, "line_start"); Role(line.EndSketchPoint, i, "line_end"); break;
                    case SketchCircle circle: Role(circle.CenterSketchPoint, i, "circle_center"); break;
                    case SketchArc arc: Role(arc.CenterSketchPoint, i, "arc_center"); Role(arc.StartSketchPoint, i, "arc_start"); Role(arc.EndSketchPoint, i, "arc_end"); break;
                }
            }
            return Ok(ctx, new JObject { ["sketch_name"] = sketch.Name, ["document"] = part.FullFileName,
                ["origin_ref"] = SketchPointReferences.Token(part, sketch, "origin"), ["points"] = points,
                ["total_points"] = sketch.SketchPoints.Count, ["truncated"] = sketch.SketchPoints.Count > limit });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
