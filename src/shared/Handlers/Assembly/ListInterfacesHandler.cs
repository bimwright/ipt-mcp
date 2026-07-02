#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Inv = global::Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class ListInterfacesHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_interfaces";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        var app = (Inv.Application)context.Application!;
        var activeDoc = app.ActiveDocument;
        if (activeDoc == null)
        {
            return Fail(context, "NO_DOCUMENT", "No active Inventor document");
        }

        string occurrenceName = (string?)parameters["occurrence"] ?? "";
        Inv.ComponentDefinition targetDef;
        Inv.ComponentOccurrence? occurrence = null;

        if (activeDoc is Inv.AssemblyDocument assemblyDoc)
        {
            var def = assemblyDoc.ComponentDefinition;
            if (!AssemblyRefResolver.TryFindOccurrence(def, occurrenceName, out occurrence, out var err))
            {
                return Fail(context, "INVALID_ARGUMENT", err!);
            }
            targetDef = occurrence != null ? (Inv.ComponentDefinition)occurrence.Definition : (Inv.ComponentDefinition)def;
        }
        else if (activeDoc is Inv.PartDocument partDoc)
        {
            if (!string.IsNullOrEmpty(occurrenceName))
            {
                return Fail(context, "INVALID_ARGUMENT", "occurrence parameter is only supported on assembly documents");
            }
            targetDef = (Inv.ComponentDefinition)partDoc.ComponentDefinition;
        }
        else
        {
            return Fail(context, "WRONG_DOCUMENT_TYPE", "Active document must be a part or assembly");
        }

        try
        {
            var imatesArr = new JArray();
            var workFeaturesArr = new JArray();
            var originArr = new JArray { "XY Plane", "XZ Plane", "YZ Plane", "X Axis", "Y Axis", "Z Axis", "Center Point" };

            // Enumerate iMates
            try
            {
                dynamic dTarget = targetDef;
                foreach (Inv.iMateDefinition im in dTarget.iMateDefinitions)
                {
                    string iMateTypeStr = "other";
                    if (im is Inv.MateiMateDefinition) iMateTypeStr = "mate";
                    else if (im is Inv.FlushiMateDefinition) iMateTypeStr = "flush";
                    else if (im is Inv.InsertiMateDefinition) iMateTypeStr = "insert";

                    var item = new JObject
                    {
                        ["name"] = im.Name,
                        ["type"] = iMateTypeStr,
                    };

                    object entity = ((dynamic)im).Entity;
                    item["entity_kind"] = entity.GetType().Name.Replace("FaceProxy", "").Replace("Face", "").Replace("EdgeProxy", "").Replace("Edge", "").ToLowerInvariant();

                    if (entity is Inv.Face face)
                    {
                        var range = face.Evaluator.RangeBox;
                        var min = range.MinPoint;
                        var max = range.MaxPoint;
                        var pt = app.TransientGeometry.CreatePoint((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);

                        if (occurrence != null)
                        {
                            pt.TransformBy(occurrence.Transformation);
                        }

                        var summary = new JObject
                        {
                            ["centroid_mm"] = new JArray(pt.X * 10.0, pt.Y * 10.0, pt.Z * 10.0)
                        };

                        if (face.SurfaceType == Inv.SurfaceTypeEnum.kPlaneSurface)
                        {
                            summary["kind"] = "planar";
                            var plane = (Inv.Plane)face.Geometry;
                            var normVec = plane.Normal;
                            if (face.IsParamReversed)
                            {
                                normVec = app.TransientGeometry.CreateUnitVector(-normVec.X, -normVec.Y, -normVec.Z);
                            }
                            if (occurrence != null)
                            {
                                normVec.TransformBy(occurrence.Transformation);
                            }
                            summary["normal"] = new JArray(normVec.X, normVec.Y, normVec.Z);
                        }
                        else if (face.SurfaceType == Inv.SurfaceTypeEnum.kCylinderSurface)
                        {
                            summary["kind"] = "cylindrical";
                            var cyl = (Inv.Cylinder)face.Geometry;
                            summary["radius_mm"] = cyl.Radius * 10.0;
                            var axisVec = cyl.AxisVector;
                            if (occurrence != null)
                            {
                                axisVec.TransformBy(occurrence.Transformation);
                            }
                            summary["axis"] = new JArray(axisVec.X, axisVec.Y, axisVec.Z);
                        }
                        else
                        {
                            summary["kind"] = "other";
                        }
                        item["geometry_summary"] = summary;
                    }
                    else
                    {
                        item["geometry_summary"] = new JObject { ["kind"] = "other" };
                    }

                    imatesArr.Add(item);
                }
            }
            catch { }

            // Enumerate User Work Features (excluding origin)
            try
            {
                dynamic dTarget = targetDef;
                foreach (Inv.WorkPlane wp in dTarget.WorkPlanes)
                {
                    if (!wp.Name.Contains("XY Plane") && !wp.Name.Contains("XZ Plane") && !wp.Name.Contains("YZ Plane"))
                    {
                        workFeaturesArr.Add(new JObject { ["name"] = wp.Name, ["kind"] = "work_plane" });
                    }
                }
                foreach (Inv.WorkAxis wa in dTarget.WorkAxes)
                {
                    if (!wa.Name.Contains("X Axis") && !wa.Name.Contains("Y Axis") && !wa.Name.Contains("Z Axis"))
                    {
                        workFeaturesArr.Add(new JObject { ["name"] = wa.Name, ["kind"] = "work_axis" });
                    }
                }
                foreach (Inv.WorkPoint wpt in dTarget.WorkPoints)
                {
                    if (!wpt.Name.Contains("Center Point"))
                    {
                        workFeaturesArr.Add(new JObject { ["name"] = wpt.Name, ["kind"] = "work_point" });
                    }
                }
            }
            catch { }

            return Ok(context, new JObject
            {
                ["imates"] = imatesArr,
                ["work_features"] = workFeaturesArr,
                ["origin"] = originArr
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to list interfaces: " + ex.Message);
        }
    }
}
#endif
