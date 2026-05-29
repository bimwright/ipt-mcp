#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Sketch;

/// <summary>
/// <c>add_sketch_constraint</c> — add a geometric constraint over sketch entities. The Inventor API
/// has one typed method per constraint kind (no generic "AddConstraint"), and the signatures differ:
/// parallel/perpendicular/collinear take two trailing ellipse-axis bools; horizontal/vertical take a
/// single entity + one bool; equal applies to two <c>SketchLine</c>s via <c>AddEqualLength</c>;
/// tangent takes an extra proximity-point arg (null is accepted). This handler maps the friendly
/// <c>type</c> string onto the right method and arity.
/// </summary>
public sealed class AddSketchConstraintHandler : HandlerBase, IInventorCommand
{
    public string Name => "add_sketch_constraint";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "add_sketch_constraint requires an active part document");

        var type = (p["type"]?.ToString() ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "type is required");
        if (p["entity_ids"] is not JArray ids || ids.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "entity_ids[] is required and must be non-empty");

        // arity check per constraint family
        var needsTwo = type is "coincident" or "parallel" or "perpendicular" or "tangent"
            or "concentric" or "equal" or "collinear" or "symmetric";
        if (needsTwo && ids.Count < 2)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"constraint '{type}' needs at least 2 entity_ids");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            SketchEntity E(int i) => EntityResolver.ResolveSketchEntity(sketch, ids[i].ToString());
            var g = sketch.GeometricConstraints;

            switch (type)
            {
                case "coincident": g.AddCoincident(E(0), E(1)); break;
                case "parallel": g.AddParallel(E(0), E(1), false, false); break;
                case "perpendicular": g.AddPerpendicular(E(0), E(1), false, false); break;
                case "horizontal": g.AddHorizontal(E(0), false); break;
                case "vertical": g.AddVertical(E(0), false); break;
                case "tangent": g.AddTangent(E(0), E(1), null); break;
                case "concentric": g.AddConcentric(E(0), E(1)); break;
                case "collinear": g.AddCollinear(E(0), E(1), false, false); break;
                case "symmetric":
                    if (ids.Count < 3)
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "symmetric needs 3 entity_ids: [entityOne, entityTwo, symmetryLine]");
                    g.AddSymmetry(E(0), E(1), (SketchLine)E(2));
                    break;
                case "equal":
                {
                    if (E(0) is not SketchLine l1 || E(1) is not SketchLine l2)
                        return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "equal constraint requires two sketch lines");
                    g.AddEqualLength(l1, l2);
                    break;
                }
                default:
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                        $"unknown constraint type '{type}' (coincident|parallel|perpendicular|horizontal|vertical|tangent|concentric|equal|collinear|symmetric)");
            }

            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["constraint_type"] = type,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
