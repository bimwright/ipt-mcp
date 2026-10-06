#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Feature;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>unwrap_try</c> — trial-unwrap a set of faces into a flat surface body, measure it, then roll
/// back unless <c>keep</c> is true (the effectustasi rollback-by-default pattern worth copying —
/// the caller probes manufacturability without committing a feature). <c>faces</c> is a non-empty
/// array of face ids (<c>"face:F"</c>/<c>"body:B/face:F"</c> — see <c>list_faces</c>).
/// <c>alignment</c> = origin|xy|xz|yz (default origin) maps to
/// <see cref="UnwrapResultAlignmentEnum"/>; <c>auto_face_chain</c> (default false) chains tangent
/// faces automatically; <c>merge_result_body</c> (default false) merges into the source body.
/// Unwrap is a general part feature, not sheet-metal-only, so the target is any part — the
/// sheet-metal guard is NOT applied. On failure the Inventor <c>ErrorManager.LastMessage</c> is
/// attached — it usually names the exact reason a face set cannot unwrap.
/// </summary>
public sealed class UnwrapTryHandler : HandlerBase, IInventorCommand
{
    public string Name => "unwrap_try";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, "unwrap_try", out var app, out var part, out var failure))
            return failure!;

        if (p["faces"] is not JArray faceIds || faceIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "faces is required — a non-empty array of face ids ('face:F' or 'body:B/face:F')");

        var alignStr = ((string?)p["alignment"] ?? "origin").Trim().ToLowerInvariant();
        var align = alignStr switch
        {
            "origin" => UnwrapResultAlignmentEnum.kOriginAlignment,
            "xy" => UnwrapResultAlignmentEnum.kXYPlaneAlignment,
            "xz" => UnwrapResultAlignmentEnum.kXZPlaneAlignment,
            "yz" => UnwrapResultAlignmentEnum.kYZPlaneAlignment,
            _ => (UnwrapResultAlignmentEnum)(-1),
        };
        if ((int)align < 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "alignment must be origin|xy|xz|yz");

        var autoChain = p["auto_face_chain"]?.Value<bool?>() ?? false;
        var merge = p["merge_result_body"]?.Value<bool?>() ?? false;
        var keep = p["keep"]?.Value<bool?>() ?? false;

        var def = part.ComponentDefinition;
        var coll = app.TransientObjects.CreateFaceCollection();
        try
        {
            foreach (var t in faceIds)
                coll.Add(FeatureSupport.ResolveFaceRef(def, t.ToString()));
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        try
        {
            var udef = def.Features.UnwrapFeatures.CreateDefinition(
                coll, Type.Missing, align, Type.Missing, Type.Missing, autoChain, merge);
            var uf = def.Features.UnwrapFeatures.Add(udef);

            var data = new JObject
            {
                ["feature_name"] = uf.Name,
                ["input_faces"] = faceIds.Count,
                ["kept"] = keep,
            };
            try
            {
                var body = uf.SurfaceBody;
                data["result_faces"] = body.Faces.Count;
                double cm2 = 0;
                foreach (Face f in body.Faces) cm2 += f.Evaluator.Area;
                data["result_area_mm2"] = Math.Round(UnitConvert.Cm2ToMm2(cm2), 3);
                var rb = body.RangeBox;
                data["result_bbox_mm"] = new JObject
                {
                    ["min"] = new JArray(UnitConvert.CmToMm(rb.MinPoint.X), UnitConvert.CmToMm(rb.MinPoint.Y), UnitConvert.CmToMm(rb.MinPoint.Z)),
                    ["max"] = new JArray(UnitConvert.CmToMm(rb.MaxPoint.X), UnitConvert.CmToMm(rb.MaxPoint.Y), UnitConvert.CmToMm(rb.MaxPoint.Z)),
                };
            }
            catch { }

            if (!keep) uf.Delete();
            return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            string? inventorMessage = null;
            try { inventorMessage = app.ErrorManager.LastMessage; } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR,
                inventorMessage is { Length: > 0 } ? ex.Message + " | inventor: " + inventorMessage : ex.Message);
        }
    }
}
#endif
