#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Handlers.Assembly;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

public sealed class HoleHandler : HandlerBase, IInventorCommand
{
    public string Name => "hole";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(context, Name, out var app, out var partDoc, out var failure))
        {
            return failure!;
        }

        var selObj = parameters["face"] as JObject;
        FaceSelectorSpec? spec = null;
        string specErr = "required";
        bool parsed = false;
        if (selObj != null)
        {
            parsed = FaceSelectorSpec.TryParse(selObj, out spec, out specErr);
        }
        if (!parsed || spec == null)
        {
            return Fail(context, "INVALID_ARGUMENT", "Invalid face selector: " + specErr);
        }

        var pointsMm = parameters["points_mm"] as JArray;
        if (pointsMm == null || pointsMm.Count == 0)
        {
            return Fail(context, "INVALID_ARGUMENT", "points_mm is required and must contain at least one point");
        }

        string kind = ((string?)parameters["kind"] ?? "").Trim().ToLowerInvariant();
        if (kind is not ("drilled" or "counterbore" or "countersink"))
        {
            return Fail(context, "INVALID_ARGUMENT", "kind must be drilled|counterbore|countersink");
        }

        double diameterMm = (double?)parameters["diameter_mm"] ?? 0;
        if (diameterMm <= 0)
        {
            return Fail(context, "INVALID_ARGUMENT", "diameter_mm is required and must be positive");
        }

        bool through = (bool?)parameters["through"] ?? false;
        double? depthMm = (double?)parameters["depth_mm"];

        if (through && depthMm != null)
        {
            return Fail(context, "INVALID_ARGUMENT", "through:true and depth_mm are mutually exclusive");
        }
        if (!through && depthMm == null)
        {
            return Fail(context, "INVALID_ARGUMENT", "Either through:true or depth_mm must be specified");
        }

        var def = partDoc.ComponentDefinition;

        // Optional tapped-thread metadata (flat wire keys: tapped_designation / tapped_class /
        // tapped_right_handed / tapped_full_depth / tapped_thread_depth_mm). Per the API spike the
        // verified path is: create the hole with a NUMERIC diameter, then assign hole.TapInfo
        // post-creation (NOT passing the HoleTapInfo as the Diameter argument).
        double diameterCm = UnitConvert.MmToCm(diameterMm);
        object? tapInfo = null;
        string tappedDesignation = ((string?)parameters["tapped_designation"] ?? "").Trim();
        bool wantTapped = !string.IsNullOrEmpty(tappedDesignation);

        if (wantTapped)
        {
            string threadClass = (string?)parameters["tapped_class"] ?? "6H";
            bool rightHanded = (bool?)parameters["tapped_right_handed"] ?? true;
            bool fullDepth = (bool?)parameters["tapped_full_depth"] ?? true;
            double? threadDepthMm = (double?)parameters["tapped_thread_depth_mm"];

            // Metric M-designations use the ISO Metric Profile thread family (verified in spike §4).
            string threadType = tappedDesignation.StartsWith("M", StringComparison.OrdinalIgnoreCase)
                ? "ISO Metric Profile"
                : "ANSI Unified Screw Threads";

            object threadDepthVal = Type.Missing;
            if (!fullDepth)
            {
                if (threadDepthMm == null)
                {
                    return Fail(context, "INVALID_ARGUMENT", "tapped_thread_depth_mm is required when tapped_full_depth is false");
                }
                threadDepthVal = UnitConvert.MmToCm(threadDepthMm.Value);
            }

            try
            {
                tapInfo = def.Features.HoleFeatures.CreateTapInfo(
                    rightHanded,
                    threadType,
                    tappedDesignation,
                    threadClass,
                    fullDepth,
                    threadDepthVal
                );
            }
            catch (Exception ex)
            {
                return Fail(context, "API_ERROR", "Failed to create TapInfo: " + ex.Message);
            }
        }

        // Select the face
        var face = FaceSelector.SelectFace(def, spec, out var candidates);
        if (face == null)
        {
            string errMessage = candidates.Count == 0
                ? "Face selection matches 0 faces."
                : "Face selection is ambiguous (matches " + candidates.Count + " faces).";

            var failResult = InventorCommandResult.Fail(System.Guid.Empty, "INVALID_ARGUMENT", errMessage, new InventorResponseMeta
            {
                TargetId = context.TargetId,
                InventorYear = context.InventorYear == 0 ? (int?)null : context.InventorYear
            });
            failResult.Data = new JObject { ["candidates"] = candidates };
            return failResult;
        }

        try
        {
            // Create a Sketch on the face
            var sketch = def.Sketches.Add(face);
            var pointsColl = app.TransientObjects.CreateObjectCollection();

            foreach (var ptToken in pointsMm)
            {
                var ptArray = ptToken as JArray;
                if (ptArray == null || ptArray.Count != 3)
                {
                    return Fail(context, "INVALID_ARGUMENT", "Each point in points_mm must be a 3D coordinate array [x,y,z]");
                }

                double mx = UnitConvert.MmToCm((double)ptArray[0]);
                double my = UnitConvert.MmToCm((double)ptArray[1]);
                double mz = UnitConvert.MmToCm((double)ptArray[2]);

                var modelPt = app.TransientGeometry.CreatePoint(mx, my, mz);

                // Verify point lands on the face within 0.01 mm tolerance
                var closest = face.GetClosestPointTo(modelPt);
                double distCm = app.TransientGeometry.CreateVector(modelPt.X - closest.X, modelPt.Y - closest.Y, modelPt.Z - closest.Z).Length;
                if (distCm > 0.001)
                {
                    // Face-bounds hint from the face's vertices (planar faces always have them), in mm,
                    // plus the nearest on-face point — enough for an agent to correct the point and retry.
                    double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                    bool anyVertex = false;
                    foreach (Vertex v in face.Vertices)
                    {
                        var vp = v.Point;
                        anyVertex = true;
                        if (vp.X < minX) minX = vp.X; if (vp.Y < minY) minY = vp.Y; if (vp.Z < minZ) minZ = vp.Z;
                        if (vp.X > maxX) maxX = vp.X; if (vp.Y > maxY) maxY = vp.Y; if (vp.Z > maxZ) maxZ = vp.Z;
                    }

                    string offMsg = $"Point [{ptArray[0]},{ptArray[1]},{ptArray[2]}] does not land on the selected face.";
                    var offResult = InventorCommandResult.Fail(System.Guid.Empty, "INVALID_ARGUMENT", offMsg, new InventorResponseMeta
                    {
                        TargetId = context.TargetId,
                        InventorYear = context.InventorYear == 0 ? (int?)null : context.InventorYear
                    });
                    var boundsHint = new JObject
                    {
                        ["nearest_on_face_mm"] = new JArray(UnitConvert.CmToMm(closest.X), UnitConvert.CmToMm(closest.Y), UnitConvert.CmToMm(closest.Z)),
                        ["offset_mm"] = UnitConvert.CmToMm(distCm)
                    };
                    if (anyVertex)
                    {
                        boundsHint["min"] = new JArray(UnitConvert.CmToMm(minX), UnitConvert.CmToMm(minY), UnitConvert.CmToMm(minZ));
                        boundsHint["max"] = new JArray(UnitConvert.CmToMm(maxX), UnitConvert.CmToMm(maxY), UnitConvert.CmToMm(maxZ));
                    }
                    offResult.Data = new JObject
                    {
                        ["error"] = offMsg,
                        ["bad_point_mm"] = new JArray((double)ptArray[0], (double)ptArray[1], (double)ptArray[2]),
                        ["face_bounds_hint"] = boundsHint
                    };
                    return offResult;
                }

                var skPt2d = sketch.ModelToSketchSpace(modelPt);
                var skPt = sketch.SketchPoints.Add(skPt2d);
                pointsColl.Add(skPt);
            }

            var placement = def.Features.HoleFeatures.CreateSketchPlacementDefinition(pointsColl);
            HoleFeature holeFeature;
            var dir = PartFeatureExtentDirectionEnum.kNegativeExtentDirection;

            if (kind == "drilled")
            {
                if (through)
                {
                    holeFeature = def.Features.HoleFeatures.AddDrilledByThroughAllExtent(placement, diameterCm, dir);
                }
                else
                {
                    double depthCm = UnitConvert.MmToCm(depthMm!.Value);
                    holeFeature = def.Features.HoleFeatures.AddDrilledByDistanceExtent(placement, diameterCm, depthCm, dir, false, Type.Missing);
                }
            }
            else if (kind == "counterbore")
            {
                double cboreDiameterMm = (double?)parameters["cbore_diameter_mm"] ?? 0;
                double cboreDepthMm = (double?)parameters["cbore_depth_mm"] ?? 0;
                if (cboreDiameterMm <= 0 || cboreDepthMm <= 0)
                {
                    return Fail(context, "INVALID_ARGUMENT", "cbore_diameter_mm and cbore_depth_mm are required and must be positive for counterbore holes");
                }
                double cboreDiaCm = UnitConvert.MmToCm(cboreDiameterMm);
                double cboreDepthCm = UnitConvert.MmToCm(cboreDepthMm);

                if (through)
                {
                    holeFeature = def.Features.HoleFeatures.AddCBoreByThroughAllExtent(placement, diameterCm, dir, cboreDiaCm, cboreDepthCm);
                }
                else
                {
                    double depthCm = UnitConvert.MmToCm(depthMm!.Value);
                    holeFeature = def.Features.HoleFeatures.AddCBoreByDistanceExtent(placement, diameterCm, depthCm, dir, cboreDiaCm, cboreDepthCm, false, Type.Missing);
                }
            }
            else // countersink
            {
                double csinkDiameterMm = (double?)parameters["csink_diameter_mm"] ?? 0;
                double csinkAngleDeg = (double?)parameters["csink_angle_deg"] ?? 82.0;
                if (csinkDiameterMm <= 0)
                {
                    return Fail(context, "INVALID_ARGUMENT", "csink_diameter_mm is required and must be positive for countersink holes");
                }
                double csinkDiaCm = UnitConvert.MmToCm(csinkDiameterMm);
                double csinkAngleRad = UnitConvert.DegToRad(csinkAngleDeg);

                if (through)
                {
                    holeFeature = def.Features.HoleFeatures.AddCSinkByThroughAllExtent(placement, diameterCm, dir, csinkDiaCm, csinkAngleRad);
                }
                else
                {
                    double depthCm = UnitConvert.MmToCm(depthMm!.Value);
                    holeFeature = def.Features.HoleFeatures.AddCSinkByDistanceExtent(placement, diameterCm, depthCm, dir, csinkDiaCm, csinkAngleRad, false, Type.Missing);
                }
            }

            // Apply tapped threads post-creation (spike §4: hole.TapInfo is settable and flips Tapped=true).
            if (tapInfo != null)
            {
                ((dynamic)holeFeature).TapInfo = tapInfo;
            }

            return Ok(context, new JObject
            {
                ["feature_names"] = new JArray(holeFeature.Name),
                ["hole_count"] = pointsMm.Count,
                ["tapped"] = wantTapped
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to create hole feature: " + ex.Message);
        }
    }
}
#endif
