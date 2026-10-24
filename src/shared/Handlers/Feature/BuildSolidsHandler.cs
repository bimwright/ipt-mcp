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
/// <c>build_solids</c> — builds many extrude features in one part from <see cref="SolidSpec"/> items
/// (box / tube / prism / extrude; op new | join | cut). The whole list is validated before Inventor
/// is touched, including targets against the part's existing bodies. Each item gets a hidden fixed
/// work plane (shared by items on the same plane), a sketch and an extrude; the new body is named
/// after the item so later items can target it. After each item the feature must be healthy, a new
/// item must add exactly one body, a join must grow and a cut must shrink its target — otherwise the
/// item's objects are deleted and the build stops (or continues with <c>stop_on_error=false</c>).
/// Completed items stay; the failure reports <c>next_index</c> so a retry resends only the rest.
/// One undo step ("MCP: build_solids").
/// </summary>
public sealed class BuildSolidsHandler : HandlerBase, IInventorCommand
{
    public string Name => "build_solids";
    public bool IsReadOnly => false;

    private const double VolumeAccuracy = 0.01;
    private const double MinVolumeChangeMm3 = 1e-3;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SolidSpec.TryParse(p["items"], out var spec, out var errors))
            return Invalid(ctx, errors);
        if (!ActiveDocumentSupport.TryGetPart(ctx, p, "build_solids", out var app, out var part, out var failure))
            return failure!;
        var stopOnError = p["stop_on_error"]?.Type != JTokenType.Boolean || (bool)p["stop_on_error"]!;
        var detail = p["detail"]?.Type == JTokenType.Boolean && (bool)p["detail"]!;
        var dryRun = p["dry_run"]?.Type == JTokenType.Boolean && (bool)p["dry_run"]!;
        var def = part.ComponentDefinition;

        // ---- targets and new body names against the part as it is now ----
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SurfaceBody b in def.SurfaceBodies) existing.Add(b.Name);
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in spec.Items)
        {
            var path = $"items[{it.Index}]";
            if (it.Op == "new")
            {
                if (existing.Contains(it.Name))
                    errors.Add(new JObject { ["index"] = it.Index, ["name"] = it.Name, ["path"] = path + ".name", ["message"] = $"the part already has a body named '{it.Name}'; pick another name or target it with op join/cut" });
                created.Add(it.Name);
            }
            else if (!created.Contains(it.Target!) && !existing.Contains(it.Target!))
                errors.Add(new JObject { ["index"] = it.Index, ["name"] = it.Name, ["path"] = path + ".target", ["message"] = $"no body named '{it.Target}' in the part or created by an earlier item" });
        }
        if (errors.Count > 0) return Invalid(ctx, errors);
        if (dryRun)
            return Ok(ctx, new JObject
            {
                ["dry_run"] = true, ["valid"] = true, ["items"] = spec.Items.Count,
                ["kinds"] = JObject.FromObject(spec.Items.GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.Count())),
                ["ops"] = JObject.FromObject(spec.Items.GroupBy(i => i.Op).ToDictionary(g => g.Key, g => g.Count())),
            });

        var planes = new Dictionary<string, WorkPlane>();
        var rows = new JArray();
        var failures = new JArray();
        var warnings = new JArray();
        int completed = 0, featuresBefore = def.Features.Count;
        int? nextIndex = null;
        var screen = app.ScreenUpdating;
        Transaction? tx = null;
        try
        {
            tx = app.TransactionManager.StartTransaction((_Document)(object)part, "MCP: build_solids");
            app.ScreenUpdating = false;
            foreach (var it in spec.Items)
            {
                var row = Build(app, def, it, planes, warnings);
                if (row["error"] == null) { completed++; if (detail) rows.Add(row); continue; }
                failures.Add(row);
                if (stopOnError) { nextIndex = it.Index; break; }
            }
            tx.End();
        }
        catch (Exception ex)
        {
            try { tx?.Abort(); } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR, "build_solids failed and was rolled back: " + ex.GetBaseException().Message);
        }
        finally
        {
            try { app.ScreenUpdating = screen; } catch { }
        }

        var data = new JObject
        {
            ["document"] = part.DisplayName,
            ["requested"] = spec.Items.Count,
            ["completed"] = completed,
            ["failed"] = failures.Count,
            ["skipped"] = spec.Items.Count - completed - failures.Count,
            ["features_added"] = def.Features.Count - featuresBefore,
            ["bodies_total"] = def.SurfaceBodies.Count,
        };
        if (failures.Count > 0) data["failures"] = failures;
        if (nextIndex != null) data["next_index"] = nextIndex;
        if (warnings.Count > 0) data["warnings"] = warnings;
        if (detail) data["items"] = rows;
        if (failures.Count == 0) return Ok(ctx, data);

        var first = failures[0];
        var result = Fail(ctx, InventorErrorCodes.API_ERROR,
            $"items[{first["index"]}] '{first["name"]}': {first["error"]} — {completed} item(s) before it were built and kept"
            + (nextIndex != null ? $"; fix it and resend items from index {nextIndex}" : ""));
        result.Data = data;
        return result;
    }

    private static JObject Build(Application app, PartComponentDefinition def, SolidSpec.Item it, Dictionary<string, WorkPlane> planes, JArray warnings)
    {
        var tg = app.TransientGeometry;
        var row = new JObject { ["index"] = it.Index, ["name"] = it.Name };
        var stage = "work plane";
        WorkPlane? newPlane = null;
        PlanarSketch? sketch = null;
        ExtrudeFeature? feature = null;
        try
        {
            var o = it.OriginMm.Select(UnitConvert.MmToCm).ToArray();
            var key = string.Join(",", o.Concat(it.U).Concat(it.V).Select(x => Math.Round(x, 9).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            if (!planes.TryGetValue(key, out var plane))
            {
                plane = newPlane = def.WorkPlanes.AddFixed(tg.CreatePoint(o[0], o[1], o[2]),
                    tg.CreateUnitVector(it.U[0], it.U[1], it.U[2]), tg.CreateUnitVector(it.V[0], it.V[1], it.V[2]), false);
                plane.Visible = false;
                try { plane.Name = it.Name + "_wp"; } catch { }
                planes[key] = plane;
            }

            stage = "sketch";
            sketch = def.Sketches.Add(plane, false);
            try { sketch.Name = it.Name + "_sk"; } catch { }
            // Profile (u, v) mm → sketch space: the sketch frame on a fixed plane is not guaranteed to
            // be (u, v), so measure it. A mirrored frame flips arc sweeps and the extrude direction.
            var s0 = sketch.ModelToSketchSpace(tg.CreatePoint(o[0], o[1], o[2]));
            var su = sketch.ModelToSketchSpace(tg.CreatePoint(o[0] + it.U[0], o[1] + it.U[1], o[2] + it.U[2]));
            var sv = sketch.ModelToSketchSpace(tg.CreatePoint(o[0] + it.V[0], o[1] + it.V[1], o[2] + it.V[2]));
            double ux = su.X - s0.X, uy = su.Y - s0.Y, vx = sv.X - s0.X, vy = sv.Y - s0.Y;
            var flip = ux * vy - uy * vx < 0;
            Point2d Map(double xMm, double yMm)
            {
                double x = UnitConvert.MmToCm(xMm), y = UnitConvert.MmToCm(yMm);
                return tg.CreatePoint2d(s0.X + x * ux + y * vx, s0.Y + x * uy + y * vy);
            }
            foreach (var shape in it.Profile) CreatePartHandler.Draw(app, sketch, shape, Map, flip);

            stage = "profile";
            var profile = sketch.Profiles.AddForSolid(true, null, null);

            stage = "extrude";
            var operation = it.Op switch
            {
                "join" => PartFeatureOperationEnum.kJoinOperation,
                "cut" => PartFeatureOperationEnum.kCutOperation,
                _ => PartFeatureOperationEnum.kNewBodyOperation,
            };
            var ext = def.Features.ExtrudeFeatures.CreateExtrudeDefinition(profile, operation);
            ext.SetDistanceExtent(UnitConvert.MmToCm(Math.Abs(it.DepthMm)),
                (it.DepthMm > 0) != flip ? PartFeatureExtentDirectionEnum.kPositiveExtentDirection : PartFeatureExtentDirectionEnum.kNegativeExtentDirection);
            double before = 0;
            if (it.Target != null)
            {
                var col = app.TransientObjects.CreateObjectCollection();
                var target = EntityResolver.ResolveBody(def, it.Target);
                col.Add(target);
                ext.AffectedBodies = col;
                before = UnitConvert.Cm3ToMm3(target.get_Volume(VolumeAccuracy));
            }
            var bodiesBefore = def.SurfaceBodies.Count;
            feature = def.Features.ExtrudeFeatures.Add(ext);
            try { feature.Name = it.Name + "_ext"; } catch { }

            stage = "check";
            if (feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("feature is " + EnumText.Friendly(feature.HealthStatus.ToString(), "Health"));
            var bodiesAfter = def.SurfaceBodies.Count;
            if (it.Op == "new")
            {
                if (bodiesAfter != bodiesBefore + 1)
                    throw new InvalidOperationException($"expected one new body, the part went from {bodiesBefore} to {bodiesAfter}");
                var body = def.SurfaceBodies[bodiesAfter];
                try { body.Name = it.Name; } catch { }
                if (!string.Equals(body.Name, it.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"could not name the new body '{it.Name}' (the name is taken in the browser; pick another)");
                row["volume_mm3"] = UnitConvert.Cm3ToMm3(body.get_Volume(VolumeAccuracy));
            }
            else
            {
                SurfaceBody? target = null;
                try { target = EntityResolver.ResolveBody(def, it.Target!); } catch (ArgumentException) { }
                if (target == null)
                    warnings.Add($"items[{it.Index}] '{it.Name}': target body '{it.Target}' no longer exists after the {it.Op}");
                else
                {
                    var after = UnitConvert.Cm3ToMm3(target.get_Volume(VolumeAccuracy));
                    row["volume_mm3"] = after;
                    var change = after - before;
                    if (it.Op == "cut" && change > -MinVolumeChangeMm3)
                        throw new InvalidOperationException($"cut removed nothing from '{it.Target}' — the profile/depth misses the body");
                    if (it.Op == "join" && change < MinVolumeChangeMm3)
                        throw new InvalidOperationException($"join added nothing to '{it.Target}' — the solid lies inside the body");
                }
                if (bodiesAfter != bodiesBefore)
                    warnings.Add($"items[{it.Index}] '{it.Name}': the {it.Op} changed the body count from {bodiesBefore} to {bodiesAfter} (a body was split or merged)");
            }
            row["feature"] = feature.Name;
            return row;
        }
        catch (Exception ex)
        {
            row["stage"] = stage;
            row["error"] = $"{stage} failed: {ex.GetBaseException().Message}";
            row["hint"] = Hint(stage, it);
            // Leave nothing of the failed item behind; a shared plane stays for the items that use it.
            try { feature?.Delete(false, false, false); } catch { }
            try { sketch?.Delete(); } catch { }
            if (newPlane != null)
            {
                try { foreach (var kv in planes.Where(kv => kv.Value == newPlane).ToList()) planes.Remove(kv.Key); newPlane.Delete(); } catch { }
            }
            return row;
        }
    }

    private static string Hint(string stage, SolidSpec.Item it) => stage switch
    {
        "profile" => "the sketch did not close into a solid profile; check the profile loops",
        "extrude" when it.Op != "new" => $"the {it.Op} must intersect body '{it.Target}'; check the position, depth sign and target",
        "extrude" => "Inventor rejected the extrude; check depth and profile",
        "check" when it.Op == "cut" => "check the cut position and the depth sign (prism: from->to; extrude: along x_axis cross y_axis)",
        _ => "check this item's geometry",
    };

    private static InventorCommandResult Invalid(InventorCommandContext ctx, JArray errors)
    {
        var first = errors.Count > 0 ? $"{errors[0]["path"]}: {errors[0]["message"]}" : "invalid items";
        var result = Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
            errors.Count == 1 ? first : $"{errors.Count} problem(s); first: {first} — nothing was built");
        result.Data = new JObject { ["errors"] = errors };
        return result;
    }
}
#endif
