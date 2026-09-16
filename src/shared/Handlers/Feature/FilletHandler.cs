#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>fillet</c> — add a constant-radius edge fillet. radius in mm (→ cm). Edges come from either
/// <c>edge_ids</c> (or <c>edges</c> as an array) — positional ids resolved by
/// <see cref="EntityResolver.ResolveEdge"/> — or <c>edges</c> as a selector object
/// (<see cref="EdgeSelectorSpec"/>: kind=circular, radius_mm/center_mm/on_body/
/// adjacent_surface_types) resolved by <see cref="EdgeSelector"/>. Uses
/// <c>FilletFeatures.AddSimple</c>. Returns the new fillet feature name and the edges it consumed.
/// </summary>
public sealed class FilletHandler : HandlerBase, IInventorCommand
{
    public string Name => "fillet";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "fillet", out var app, out var part, out var failure))
            return failure!;

        if (p["radius_mm"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "radius_mm is required");
        var radiusMm = p.Value<double>("radius_mm");
        if (radiusMm <= 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "radius_mm must be greater than 0");

        var edgesToken = p["edges"];
        if (edgesToken is null || edgesToken.Type == JTokenType.Null) edgesToken = p["edge_ids"];
        if (edgesToken is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "edges is required — an array of edge ids or a selector object {kind:'circular', …}");

        var def = part.ComponentDefinition;
        var edges = app.TransientObjects.CreateEdgeCollection();
        const int MaxMatchedRows = 200;   // cap emitted rows — the write commits before the
        var matchedTruncated = false;      // size guard could reject an oversized payload
        JArray matched;
        try
        {
            if (edgesToken is JArray ids)
            {
                if (ids.Count == 0)
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edges[] must be non-empty");
                matched = new JArray();
                foreach (var token in ids)
                {
                    var edge = EntityResolver.ResolveEdge(def, token.ToString());
                    edges.Add(edge);
                    matched.Add(token.ToString());
                }
            }
            else
            {
                if (!EdgeSelectorSpec.TryParse(edgesToken, out var spec, out var selErr))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, selErr);
                var matches = EdgeSelector.Select(def, spec);
                if (matches.Count == 0)
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edge selector matched no edges");
                matched = new JArray();
                foreach (var m in matches)
                {
                    edges.Add(m.Edge);
                    if (matched.Count < MaxMatchedRows) matched.Add(m.Json);
                    else matchedTruncated = true;
                }
            }
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        try
        {
            var feature = def.Features.FilletFeatures.AddSimple(
                edges,
                UnitConvert.MmToCm(radiusMm),
                AllFillets: false, AllRounds: false,
                AutomaticEdgeChain: true,
                RollAlongSharpEdges: true,
                RollingBallWherePossible: true,
                PreserveAllFeatures: false);
            var data = new JObject
            {
                ["feature_name"] = feature.Name,
                ["radius_mm"] = radiusMm,
                ["edge_count"] = edges.Count,
                ["matched_edges"] = matched,
            };
            if (matchedTruncated) data["matched_edges_truncated"] = true;
            return Ok(ctx, data);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
