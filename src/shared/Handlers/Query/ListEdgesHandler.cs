#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Query;

/// <summary>
/// <c>list_edges</c> — read-only edge enumeration of the target part's solid bodies. Rows are
/// <c>{id:"body:B/edge:E", curve_type, length_mm, radius_mm?}</c>; ids round-trip into the
/// <c>edge:E</c>/<c>body:B/edge:E</c> refs accepted by fillet, chamfer, create_work_axis and
/// sheet_fabrication_flange. <c>curve_type</c> is the lowercase enum core (line|line_segment|circle|
/// circular_arc|ellipse_full|elliptical_arc|bspline|polyline|unknown); <c>radius_mm</c> is
/// emitted for circular edges only. <c>body</c> scopes to one body ("1"/"body:N"/name),
/// <c>curve_type</c> filters by the same lowercase names, <c>max_items</c> caps the list
/// (default 200) and <c>truncated</c> reports overflow. One sick edge yields a stub row rather
/// than failing the list — the id column must stay aligned with the 1-based collection index.
/// </summary>
public sealed class ListEdgesHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_edges";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, "list_edges", out var app, out var part, out var failure))
            return failure!;

        int maxItems;
        try { maxItems = (int?)p["max_items"] ?? 200; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "max_items must be an integer"); }
        if (maxItems <= 0) maxItems = 200;
        var typeFilter = (p["curve_type"]?.Type == JTokenType.String) ? ((string)p["curve_type"]!).Trim().ToLowerInvariant() : null;

        try
        {
            var def = part.ComponentDefinition;
            var bodies = def.SurfaceBodies;
            var arr = new JArray();
            var total = 0;
            for (var bi = 1; bi <= bodies.Count; bi++)
            {
                SurfaceBody body;
                if (IsPresent(p["body"]))
                {
                    var only = EntityResolver.ResolveBody(def, p["body"]!.ToString());
                    if (!ReferenceEquals(only, bodies[bi])) continue;
                    body = only;
                }
                else body = bodies[bi];

                var edges = body.Edges;
                for (var ei = 1; ei <= edges.Count; ei++)
                {
                    Edge e;
                    try { e = edges[ei]; }
                    catch { continue; }

                    var ct = CurveTypeName(e);
                    if (typeFilter is not null && ct != typeFilter) continue;
                    total++;
                    if (arr.Count >= maxItems) continue;

                    var row = new JObject
                    {
                        ["id"] = $"body:{bi}/edge:{ei}",
                        ["curve_type"] = ct,
                    };
                    try
                    {
                        var ev = e.Evaluator;
                        ev.GetParamExtents(out var min, out var max);
                        ev.GetLengthAtParam(min, max, out var lenCm);
                        row["length_mm"] = Math.Round(UnitConvert.CmToMm(lenCm), 3);
                    }
                    catch { }
                    try
                    {
                        switch (e.Geometry)
                        {
                            case Circle c: row["radius_mm"] = Math.Round(UnitConvert.CmToMm(c.Radius), 3); break;
                            case Arc3d a: row["radius_mm"] = Math.Round(UnitConvert.CmToMm(a.Radius), 3); break;
                        }
                    }
                    catch { }
                    arr.Add(row);
                }
            }

            return Ok(ctx, new JObject
            {
                ["edges"] = arr,
                ["total"] = total,
                ["truncated"] = total > arr.Count,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }

    internal static string CurveTypeName(Edge e)
    {
        try
        {
            var s = EnumText.Friendly(e.CurveType.ToString(), "Curve").ToLowerInvariant();
            return s switch
            {
                "linesegment" => "line_segment",
                "circulararc" => "circular_arc",
                "ellipsefull" => "ellipse_full",
                "ellipticalarc" => "elliptical_arc",
                _ => s,
            };
        }
        catch { return "unknown"; }
    }

    private static bool IsPresent(JToken? t) => t is not null && t.Type != JTokenType.Null && t.Type != JTokenType.Undefined;
}
#endif
