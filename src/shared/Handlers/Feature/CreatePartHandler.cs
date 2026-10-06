#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// <c>create_part</c> (E2) — build a whole part from a <see cref="PartRecipe"/> in one call: new
/// part → user parameters → sketches (drawn here so polyline/arc vertices share sketch points and
/// the profile closes) → extrude / hole / fillet / chamfer through the existing wire handlers →
/// material + iProperties → silent Save-As. Any failing step closes the new part unsaved and the
/// error names the recipe path. <c>dry_run</c> validates and returns the plan without Inventor work.
/// </summary>
public sealed class CreatePartHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_part";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!PartRecipe.TryParse(p["recipe"], out var recipe, out var parseError))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, parseError!);
        var dryRun = p["dry_run"]?.Type == JTokenType.Boolean && (bool)p["dry_run"]!;
        if (recipe.SaveAs != null)
        {
            if (!System.IO.Path.IsPathRooted(recipe.SaveAs))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "recipe.save_as: must be an absolute path");
            if (System.IO.File.Exists(recipe.SaveAs) && !recipe.Overwrite)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"recipe.save_as: '{recipe.SaveAs}' already exists; set overwrite:true to replace it");
        }
        if (dryRun)
            return Ok(ctx, new JObject { ["dry_run"] = true, ["valid"] = true, ["steps"] = recipe.Plan() });

        var app = (Application)ctx.Application!;
        if (ctx.Commands is null) return Fail(ctx, InventorErrorCodes.API_ERROR, "command registry unavailable");

        // An open document already at save_as would make SaveAs fail late — refuse up front.
        if (recipe.SaveAs != null)
        {
            foreach (global::Inventor.Document d in app.Documents)
            {
                string? f = null;
                try { f = d.FullFileName; } catch { }
                if (string.Equals(f, recipe.SaveAs, StringComparison.OrdinalIgnoreCase))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"recipe.save_as: '{recipe.SaveAs}' is open in Inventor; close it first");
            }
        }

        var created = Run(ctx, "new_part", new JObject { ["template"] = recipe.Template }, "recipe.template");
        if (created.error != null) return created.error;
        PartDocument part;
        try { part = (PartDocument)app.ActiveDocument; }
        catch { return Fail(ctx, InventorErrorCodes.API_ERROR, "new part did not become the active document"); }

        var steps = new JArray();
        InventorCommandResult Abort(InventorCommandResult failure)
        {
            try { part.Close(true); } catch { }
            if (failure.Error != null) failure.Error.Message += " (the new part was closed without saving)";
            return failure;
        }

        try
        {
            foreach (var prm in recipe.Parameters)
            {
                var r = Run(ctx, "create_parameter", new JObject { ["name"] = prm.Name, ["expression"] = prm.Expression, ["unit"] = prm.Unit },
                    $"parameters[{prm.Name}]");
                if (r.error != null) return Abort(r.error);
            }

            foreach (var f in recipe.Features)
            {
                switch (f)
                {
                    case PartRecipe.SketchFeature s:
                    {
                        var err = BuildSketch(ctx, app, part, s);
                        if (err != null) return Abort(err);
                        steps.Add(new JObject { ["path"] = s.Path, ["sketch"] = s.Name });
                        break;
                    }
                    case PartRecipe.ExtrudeFeature e:
                    {
                        var ep = new JObject
                        {
                            ["sketch_name"] = e.Sketch, ["distance_mm"] = e.Distance.DeepClone(),
                            ["direction"] = e.Direction, ["operation"] = e.Operation,
                        };
                        if (e.Name != null) ep["name"] = e.Name;
                        var r = Run(ctx, "extrude", ep, e.Path);
                        if (r.error != null) return Abort(r.error);
                        steps.Add(new JObject { ["path"] = e.Path, ["feature"] = r.data?["feature_name"] ?? r.data?["name"] });
                        break;
                    }
                    case PartRecipe.PassThroughFeature pf:
                    {
                        var r = Run(ctx, pf.Command, (JObject)pf.Params.DeepClone(), pf.Path);
                        if (r.error != null) return Abort(r.error);
                        steps.Add(new JObject { ["path"] = pf.Path, ["result"] = r.data });
                        break;
                    }
                }
            }

            if (recipe.Material != null)
            {
                var r = Run(ctx, "set_material", new JObject { ["material_name"] = recipe.Material }, "recipe.material");
                if (r.error != null) return Abort(r.error);
            }
            foreach (var ip in recipe.IProperties)
            {
                var r = Run(ctx, "set_iproperty", new JObject { ["set_name"] = ip.Set, ["prop_name"] = ip.Prop, ["value"] = ip.Value },
                    $"recipe.iproperties[\"{ip.Prop}\"]");
                if (r.error != null) return Abort(r.error);
            }

            try { part.Update2(true); } catch { }

            var data = new JObject { ["title"] = part.DisplayName, ["features"] = steps };
            var mass = Run(ctx, "get_mass_properties", new JObject(), "mass").data as JObject;
            if (mass != null)
            {
                data["volume_mm3"] = mass["volume_mm3"];
                data["mass_g"] = mass["mass_g"];
                data["bbox_mm"] = mass["bbox_mm"] ?? mass["bounding_box_mm"];
            }
            data["bodies"] = part.ComponentDefinition.SurfaceBodies.Count;
            var sick = SickFeatures(part);
            if (sick.Count > 0) data["unhealthy_features"] = sick;

            if (recipe.SaveAs != null)
            {
                var r = Run(ctx, "save_document", new JObject { ["path"] = recipe.SaveAs, ["silent"] = true }, "recipe.save_as");
                if (r.error != null) return Abort(r.error);
                data["path"] = recipe.SaveAs;
                if (recipe.CloseAfter)
                {
                    try { part.Close(true); data["closed"] = true; } catch (Exception ex) { data["close_error"] = ex.Message; }
                }
            }
            else
            {
                data["path"] = null;
                data["note"] = "part left open and unsaved (no save_as)";
            }
            return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            return Abort(Fail(ctx, InventorErrorCodes.API_ERROR, "create_part failed: " + ex.Message));
        }
    }

    private static (JToken? data, InventorCommandResult? error) Run(InventorCommandContext ctx, string command, JObject p, string path)
    {
        if (!ctx.Commands!.TryGetValue(command, out var h))
            return (null, InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.API_ERROR, $"{path}: {command} unavailable", new InventorResponseMeta { TargetId = ctx.TargetId }));
        var r = h.Execute(ctx, p);
        if (r.Ok) return (r.Data, null);
        r.Error!.Message = $"{path}: {r.Error.Message}";
        return (null, r);
    }

    private InventorCommandResult? BuildSketch(InventorCommandContext ctx, Application app, PartDocument part, PartRecipe.SketchFeature s)
    {
        var plane = s.PlaneName;
        if (s.PlanePose is { } pose)
        {
            plane = s.Name + "_WP";
            var wp = Run(ctx, "create_work_plane", new JObject
            {
                ["type"] = "fixed",
                ["origin"] = new JArray(pose.OriginCm[0] * 10, pose.OriginCm[1] * 10, pose.OriginCm[2] * 10),
                ["x_axis"] = new JArray(pose.X),
                ["y_axis"] = new JArray(pose.Y),
                ["name"] = plane,
                ["visible"] = false,
            }, s.Path + ".plane");
            if (wp.error != null) return wp.error;
        }
        var created = Run(ctx, "create_sketch", new JObject { ["plane"] = plane }, s.Path + ".plane");
        if (created.error != null) return created.error;
        var def = part.ComponentDefinition;
        var sketch = def.Sketches[def.Sketches.Count];
        try { sketch.Name = s.Name; }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"{s.Path}.name: {ex.Message}"); }

        try
        {
            var i = 0;
            foreach (var shape in s.Shapes)
            {
                Draw(app, sketch, shape);
                i++;
            }
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, $"{s.Path}.profile: drawing failed: {ex.Message}");
        }
        return null;
    }

    private static void Draw(Application app, PlanarSketch sketch, PartRecipe.Shape shape)
    {
        var tg = app.TransientGeometry;
        Point2d P(double xMm, double yMm) => tg.CreatePoint2d(UnitConvert.MmToCm(xMm), UnitConvert.MmToCm(yMm));
        switch (shape)
        {
            case PartRecipe.Rect r:
                sketch.SketchLines.AddAsTwoPointRectangle(P(r.X1, r.Y1), P(r.X2, r.Y2));
                break;
            case PartRecipe.Circle c:
                sketch.SketchCircles.AddByCenterRadius(P(c.Cx, c.Cy), UnitConvert.MmToCm(c.R));
                break;
            case PartRecipe.Polyline pl:
            {
                var pts = pl.Points.Select(v => sketch.SketchPoints.Add(P(v.X, v.Y), false)).ToList();
                for (var i = 0; i < pts.Count; i++)
                {
                    var a = pl.Points[i];
                    var b = pl.Points[(i + 1) % pts.Count];
                    var sa = pts[i];
                    var sb = pts[(i + 1) % pts.Count];
                    if (Math.Abs(a.Bulge) < 1e-12)
                    {
                        sketch.SketchLines.AddByTwoPoints(sa, sb);
                    }
                    else
                    {
                        var (cx, cy) = BulgeCenter(a.X, a.Y, b.X, b.Y, a.Bulge);
                        sketch.SketchArcs.AddByCenterStartEndPoint(P(cx, cy), sa, sb, a.Bulge > 0);
                    }
                }
                break;
            }
        }
        foreach (var inner in shape.Inner) Draw(app, sketch, inner);
    }

    /// <summary>Arc center for a DXF-style bulge (tan(sweep/4); positive = counter-clockwise a→b).</summary>
    internal static (double x, double y) BulgeCenter(double ax, double ay, double bx, double by, double bulge)
    {
        double dx = bx - ax, dy = by - ay;
        var chord = Math.Sqrt(dx * dx + dy * dy);
        double mx = (ax + bx) / 2, my = (ay + by) / 2;
        // left normal of a→b
        double nx = -dy / chord, ny = dx / chord;
        var offset = chord / 2 * (1 - bulge * bulge) / (2 * bulge);
        return (mx + nx * offset, my + ny * offset);
    }

    private static JArray SickFeatures(PartDocument part)
    {
        var sick = new JArray();
        foreach (PartFeature f in part.ComponentDefinition.Features)
        {
            try
            {
                if (f.HealthStatus != HealthStatusEnum.kUpToDateHealth && !f.Suppressed)
                    sick.Add(new JObject { ["name"] = f.Name, ["health"] = EnumText.Friendly(f.HealthStatus.ToString(), "Health") });
            }
            catch { }
        }
        return sick;
    }
}
#endif
