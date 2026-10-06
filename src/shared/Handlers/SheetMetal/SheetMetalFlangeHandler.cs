#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Feature;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sm_flange</c> — bend a flange up from sheet edges. <c>edges</c> is a non-empty array of edge
/// ids (<c>"edge:E"</c>/<c>"body:B/edge:E"</c> — see <c>list_edges</c>; straight sheet-boundary
/// edges). <c>angle_deg</c> defaults 90; <c>height</c> accepts a number (mm) or an expression
/// string (<c>"25 mm"</c>, <c>"flange_h"</c>) — passed to
/// <c>FlangeFeatures.CreateFlangeDefinition</c> as an expression <b>string</b>, the Variant form
/// Autodesk documents, which also dodges the ignored-Distance quirk competitors hit when passing
/// a raw double on Inventor 2026. After <c>Add</c> the applied height is read back from
/// <c>HeightExtent.Distance.Expression</c> and rewritten once if Inventor dropped the argument.
/// Optional <c>bend_radius_mm</c> overrides the style bend radius for this flange.
/// Returns feature_name + fresh face/edge counts.
/// </summary>
public sealed class SheetMetalFlangeHandler : HandlerBase, IInventorCommand
{
    public string Name => "sm_flange";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetSheetMetalPart(ctx, p, "sm_flange", out var app, out var part, out var smDef, out var failure))
            return failure!;

        if (p["edges"] is not JArray edgeIds || edgeIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "edges is required — a non-empty array of edge ids ('edge:E' or 'body:B/edge:E')");

        var angleDeg = p["angle_deg"]?.Value<double?>() ?? 90.0;
        if (angleDeg <= 0 || angleDeg > 180)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "angle_deg must be in (0, 180]");

        var heightToken = p["height"] ?? p["height_mm"];
        string heightExpr;
        if (heightToken is null || heightToken.Type == JTokenType.Null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "height is required (mm number or expression like '25 mm')");
        else if (heightToken.Type == JTokenType.Integer || heightToken.Type == JTokenType.Float)
        {
            var h = heightToken.Value<double>();
            if (double.IsNaN(h) || double.IsInfinity(h) || h <= 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "height must be a finite number greater than 0");
            heightExpr = h.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " mm";
        }
        else if (heightToken.Type == JTokenType.String)
        {
            heightExpr = ((string)heightToken!).Trim();
            if (heightExpr.Length == 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "height expression is empty");
        }
        else
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "height must be a number (mm) or an expression string");

        EdgeCollection edges;
        try { edges = FeatureSupport.EdgeCollection(app, part.ComponentDefinition, edgeIds); }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        try
        {
            var smFeatures = SheetMetalSupport.SmFeatures(smDef)
                ?? throw new InvalidOperationException("sheet-metal features are unavailable");
            var angleExpr = angleDeg.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " deg";
            var flangeDef = smFeatures.FlangeFeatures.CreateFlangeDefinition(edges, angleExpr, heightExpr);
            var feature = smFeatures.FlangeFeatures.Add(flangeDef);

            var data = new JObject
            {
                ["feature_name"] = feature.Name,
                ["angle_deg"] = angleDeg,
                ["edge_count"] = edges.Count,
            };

            // Post-verify: if the height Variant was dropped on Add (the competitor's 2026 quirk),
            // rewrite the expression on the applied distance extent.
            try
            {
                if (feature.Definition.HeightExtent is DistanceExtent dist)
                {
                    var applied = dist.Distance.Expression;
                    data["height_expression"] = applied;
                    if (!string.Equals(applied.Trim(), heightExpr, StringComparison.OrdinalIgnoreCase))
                    {
                        dist.Distance.Expression = heightExpr;
                        data["height_expression"] = dist.Distance.Expression;
                        data["height_rewritten"] = true;
                    }
                }
            }
            catch { }

            var bend = p["bend_radius_mm"]?.Value<double?>();
            if (bend is > 0)
            {
                try
                {
                    feature.Definition.BendRadius =
                        bend.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " mm";
                    data["bend_radius_mm"] = bend.Value;
                }
                catch (Exception ex) { data["bend_radius_error"] = ex.Message; }
            }

            var requestedName = (string?)p["name"];
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                try { feature.Name = requestedName; data["feature_name"] = feature.Name; } catch { }
            }
            data.Merge(SheetMetalSupport.TopologyCounts(part.ComponentDefinition));
            return Ok(ctx, data);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
