#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
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

            Inv.iMateDefinitions? iMates = null;
            Inv.WorkPlanes? workPlanes = null;
            Inv.WorkAxes? workAxes = null;
            Inv.WorkPoints? workPoints = null;

            if (targetDef is Inv.PartComponentDefinition partDef)
            {
                iMates = partDef.iMateDefinitions;
                workPlanes = partDef.WorkPlanes;
                workAxes = partDef.WorkAxes;
                workPoints = partDef.WorkPoints;
            }
            else if (targetDef is Inv.AssemblyComponentDefinition asmDef)
            {
                iMates = asmDef.iMateDefinitions;
                workPlanes = asmDef.WorkPlanes;
                workAxes = asmDef.WorkAxes;
                workPoints = asmDef.WorkPoints;
            }

            // Enumerate iMates
            if (iMates != null)
            {
                foreach (Inv.iMateDefinition im in iMates)
                {
                    try
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

                        // Inventor 2027 only exposes the type-testable Face/Edge through the runtime
                        // Entity member; ReferencedEntity is an opaque COM object.
                        object entity = ((dynamic)im).Entity;
                        string entityKind = "unknown";
                        if (entity is Inv.Face) entityKind = "face";
                        else if (entity is Inv.Edge) entityKind = "edge";
                        else if (entity is Inv.WorkPlane) entityKind = "work_plane";
                        else if (entity is Inv.WorkAxis) entityKind = "work_axis";
                        else if (entity is Inv.WorkPoint) entityKind = "work_point";

                        item["entity_kind"] = entityKind;

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
                                ["centroid_mm"] = new JArray(UnitConvert.CmToMm(pt.X), UnitConvert.CmToMm(pt.Y), UnitConvert.CmToMm(pt.Z))
                            };

                            if (face.SurfaceType == Inv.SurfaceTypeEnum.kPlaneSurface)
                            {
                                summary["kind"] = "planar";
                                var plane = (Inv.Plane)face.Geometry;
                                var localNorm = app.TransientGeometry.CreateVector(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);
                                if (face.IsParamReversed)
                                {
                                    localNorm.ScaleBy(-1.0);
                                }
                                if (occurrence != null)
                                {
                                    localNorm.TransformBy(occurrence.Transformation);
                                }
                                summary["normal"] = new JArray(localNorm.X, localNorm.Y, localNorm.Z);
                            }
                            else if (face.SurfaceType == Inv.SurfaceTypeEnum.kCylinderSurface)
                            {
                                summary["kind"] = "cylindrical";
                                var cyl = (Inv.Cylinder)face.Geometry;
                                summary["radius_mm"] = UnitConvert.CmToMm(cyl.Radius);
                                var localAxis = app.TransientGeometry.CreateVector(cyl.AxisVector.X, cyl.AxisVector.Y, cyl.AxisVector.Z);
                                if (occurrence != null)
                                {
                                    localAxis.TransformBy(occurrence.Transformation);
                                }
                                summary["axis"] = new JArray(localAxis.X, localAxis.Y, localAxis.Z);
                            }
                            else
                            {
                                summary["kind"] = "other";
                            }
                            item["geometry_summary"] = summary;
                        }
                        else if (entity is Inv.Edge edge)
                        {
                            var summary = new JObject { ["kind"] = "edge" };
                            try
                            {
                                if (edge.GeometryType == Inv.CurveTypeEnum.kCircleCurve)
                                {
                                    var circle = (Inv.Circle)edge.Geometry;
                                    var center = circle.Center;
                                    var normal = circle.Normal;
                                    if (occurrence != null)
                                    {
                                        center.TransformBy(occurrence.Transformation);
                                        normal.TransformBy(occurrence.Transformation);
                                    }
                                    summary["curve"] = "circle";
                                    summary["center_mm"] = new JArray(
                                        UnitConvert.CmToMm(center.X), UnitConvert.CmToMm(center.Y), UnitConvert.CmToMm(center.Z));
                                    summary["radius_mm"] = UnitConvert.CmToMm(circle.Radius);
                                    summary["normal"] = new JArray(normal.X, normal.Y, normal.Z);
                                }
                            }
                            catch { }
                            item["geometry_summary"] = summary;
                        }
                        else
                        {
                            item["geometry_summary"] = new JObject { ["kind"] = "other" };
                        }

                        imatesArr.Add(item);
                    }
                    catch { }
                }
            }

            // Enumerate User Work Features (excluding origin)
            try
            {
                if (workPlanes != null)
                {
                    foreach (Inv.WorkPlane wp in workPlanes)
                    {
                        if (!wp.Name.Contains("XY Plane") && !wp.Name.Contains("XZ Plane") && !wp.Name.Contains("YZ Plane"))
                        {
                            workFeaturesArr.Add(new JObject { ["name"] = wp.Name, ["kind"] = "work_plane" });
                        }
                    }
                }
                if (workAxes != null)
                {
                    foreach (Inv.WorkAxis wa in workAxes)
                    {
                        if (!wa.Name.Contains("X Axis") && !wa.Name.Contains("Y Axis") && !wa.Name.Contains("Z Axis"))
                        {
                            workFeaturesArr.Add(new JObject { ["name"] = wa.Name, ["kind"] = "work_axis" });
                        }
                    }
                }
                if (workPoints != null)
                {
                    foreach (Inv.WorkPoint wpt in workPoints)
                    {
                        if (!wpt.Name.Contains("Center Point"))
                        {
                            workFeaturesArr.Add(new JObject { ["name"] = wpt.Name, ["kind"] = "work_point" });
                        }
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
