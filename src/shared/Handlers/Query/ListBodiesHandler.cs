#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Query;

/// <summary>
/// <c>list_bodies</c> — read-only (spec F4-P0-3). Lists the active part's solid bodies as
/// <c>{id:"body:N", name, volume_mm3, bbox_mm:{min:[x,y,z],max:[x,y,z]}, face_count, created_by,
/// visible}</c>. <c>id</c>/<c>name</c> round-trip into <c>affected_bodies</c> / <c>ResolveBody</c>.
/// <c>created_by</c> is the producing feature name (<see cref="SurfaceBody.CreatedByFeature"/>),
/// null for bodies without one (e.g. imports). A sick body yields missing fields rather than
/// failing the whole list (worst case an id-only stub row keeps the index alignment).
/// <c>max_items</c> caps the list; <c>total</c>+<c>truncated</c> report the model count.
/// </summary>
public sealed class ListBodiesHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_bodies";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, "list_bodies", out var app, out var part, out var failure))
            return failure!;

        int maxItems = (int?)p["max_items"] ?? 200;
        if (maxItems <= 0) maxItems = 200;

        try
        {
            var bodies = part.ComponentDefinition.SurfaceBodies;
            var arr = new JArray();
            var total = bodies.Count;
            for (var i = 1; i <= Math.Min(total, maxItems); i++)
            {
                // One sick body must not fail the list — even a stub row keeps ids aligned with
                // the 1-based collection indices the rest of the surface addresses bodies by.
                var row = new JObject { ["id"] = $"body:{i}" };
                try
                {
                    var b = bodies[i];
                    row["name"] = b.Name;
                    row["visible"] = b.Visible;

                    try { row["volume_mm3"] = UnitConvert.Cm3ToMm3(b.get_Volume(0.01)); } catch { }
                    try
                    {
                        var rb = b.RangeBox;
                        row["bbox_mm"] = new JObject
                        {
                            ["min"] = new JArray(UnitConvert.CmToMm(rb.MinPoint.X), UnitConvert.CmToMm(rb.MinPoint.Y), UnitConvert.CmToMm(rb.MinPoint.Z)),
                            ["max"] = new JArray(UnitConvert.CmToMm(rb.MaxPoint.X), UnitConvert.CmToMm(rb.MaxPoint.Y), UnitConvert.CmToMm(rb.MaxPoint.Z)),
                        };
                    }
                    catch { }
                    try { row["face_count"] = b.Faces.Count; } catch { }
                    try { row["created_by"] = b.CreatedByFeature?.Name; } catch { }
                }
                catch { /* name/visible themselves threw — emit the id-only stub row */ }
                arr.Add(row);
            }

            return Ok(ctx, new JObject
            {
                ["bodies"] = arr,
                ["total"] = total,
                ["truncated"] = total > maxItems,
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
