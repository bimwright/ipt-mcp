#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Assembly;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    private static JToken SketchColor(Color? color) => color == null ? JValue.CreateNull() : new JArray(color.Red, color.Green, color.Blue);

    private static JObject SketchBox(DrawingSketch sketch)
    {
        var boxes = sketch.SketchEntities.Cast<SketchEntity>().Select(x => x.RangeBox).Concat(sketch.TextBoxes.Cast<TextBox>().Select(x => x.RangeBox));
        var app = (Application)sketch.Application;
        var corners = boxes.SelectMany(box => new[] { box.MinPoint, box.MaxPoint, app.TransientGeometry.CreatePoint2d(box.MinPoint.X, box.MaxPoint.Y), app.TransientGeometry.CreatePoint2d(box.MaxPoint.X, box.MinPoint.Y) }).Select(sketch.SketchToSheetSpace).ToArray();
        return new JObject { ["min"] = new JArray(corners.Min(x => x.X) * 10, corners.Min(x => x.Y) * 10), ["max"] = new JArray(corners.Max(x => x.X) * 10, corners.Max(x => x.Y) * 10) };
    }

    private static JObject SketchContent(DrawingSketch sketch)
    {
        var rows = new JArray();
        foreach (SketchLine line in sketch.SketchLines) rows.Add(new JObject { ["kind"] = "line", ["from"] = DrawingGeometry.PointInfo(line.StartSketchPoint.Geometry), ["to"] = DrawingGeometry.PointInfo(line.EndSketchPoint.Geometry), ["layer"] = line.Layer.Name, ["construction"] = line.Construction });
        foreach (SketchCircle circle in sketch.SketchCircles) rows.Add(new JObject { ["kind"] = "circle", ["center"] = DrawingGeometry.PointInfo(circle.CenterSketchPoint.Geometry), ["radius_mm"] = circle.Geometry.Radius * 10, ["layer"] = circle.Layer.Name, ["construction"] = circle.Construction });
        foreach (SketchArc arc in sketch.SketchArcs) rows.Add(new JObject { ["kind"] = "arc", ["center"] = DrawingGeometry.PointInfo(arc.CenterSketchPoint.Geometry), ["from"] = DrawingGeometry.PointInfo(arc.StartSketchPoint.Geometry), ["to"] = DrawingGeometry.PointInfo(arc.EndSketchPoint.Geometry), ["radius_mm"] = arc.Geometry.Radius * 10, ["sweep_deg"] = arc.Geometry.SweepAngle * 180 / Math.PI, ["layer"] = arc.Layer.Name, ["construction"] = arc.Construction });
        foreach (TextBox text in sketch.TextBoxes) rows.Add(new JObject { ["kind"] = "text", ["text"] = text.Text, ["formatted_text"] = text.FormattedText, ["position"] = DrawingGeometry.PointInfo(text.Origin), ["rotation_deg"] = text.Rotation * 180 / Math.PI, ["font_size_mm"] = text.Style.FontSize * 10, ["layer"] = text.Layer.Name, ["color_rgb"] = new JArray(text.Color.Red, text.Color.Green, text.Color.Blue) });
        var curves = new JArray();
        foreach (SketchLine line in sketch.SketchLines) curves.Add(new JObject { ["color"] = SketchColor(line.OverrideColor), ["weight_mm"] = line.LineWeight * 10, ["line_type"] = line.LineType.ToString(), ["sketch_only"] = line.SketchOnly });
        foreach (SketchCircle circle in sketch.SketchCircles) curves.Add(new JObject { ["color"] = SketchColor(circle.OverrideColor), ["weight_mm"] = circle.LineWeight * 10, ["line_type"] = circle.LineType.ToString(), ["sketch_only"] = circle.SketchOnly });
        foreach (SketchArc arc in sketch.SketchArcs) curves.Add(new JObject { ["color"] = SketchColor(arc.OverrideColor), ["weight_mm"] = arc.LineWeight * 10, ["line_type"] = arc.LineType.ToString(), ["sketch_only"] = arc.SketchOnly });
        return new JObject { ["name"] = sketch.Name, ["visible"] = sketch.Visible, ["native_entity_count"] = sketch.SketchEntities.Count, ["points"] = new JArray(sketch.SketchPoints.Cast<SketchPoint>().Select(point => DrawingGeometry.PointInfo(point.Geometry))), ["constraint_count"] = sketch.GeometricConstraints.Count + sketch.DimensionConstraints.Count, ["color_rgb"] = SketchColor(sketch.Color), ["weight_mm"] = sketch.LineWeight * 10, ["curve_attributes"] = curves, ["entities"] = rows };
    }

    private static InventorCommandResult SketchOnView(InventorCommandContext ctx, DrawingDocument d, Sheet sheet, JObject p)
    {
        DrawingSupport.RequireAnnotations(sheet);
        var app = DrawingSupport.App(ctx);
        if (app.ActiveDocument == null || !app.ActiveDocument.Equals(d) || !d.ActiveSheet.Equals(sheet) || !sheet.Equals(app.ActiveEditObject)) throw new ArgumentException("sketch_on_view requires the selected drawing/sheet to be active with no object open for edit.");
        var view = DrawingInput.Present(p, "view") ? DrawingSupport.View(sheet, p.Value<string>("view")) : null;
        if (view != null && (view.Suppressed || !view.UpToDate)) throw new ArgumentException("The target view must be available and up to date.");
        var sketches = view == null ? sheet.Sketches : view.Sketches;
        var name = p.Value<string>("name")!;
        var existing = sketches.Cast<DrawingSketch>().Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (existing.Length > 0)
        {
            if (existing.Length != 1) throw new ArgumentException("Sketch name is ambiguous.");
            DrawingSupport.Existing(existing[0].AttributeSets, p);
            var content = SketchContent(existing[0]);
            if (DrawingSupport.Read(existing[0].AttributeSets, "native_hash") != DrawingGeometry.Hash(content.ToString(Formatting.None))) throw new ArgumentException("Managed sketch was edited. Use a new name or edit it explicitly.");
            return DrawingSupport.Success(ctx, new JObject { ["created"] = false, ["existing"] = true, ["name"] = name, ["view"] = view?.Name, ["sheet"] = sheet.Name, ["space"] = p.Value<string>("space"), ["entity_count"] = ((JArray)content["entities"]!).Count, ["follows_view"] = view != null, ["box_mm"] = SketchBox(existing[0]), ["content"] = content, ["document_unchanged"] = true });
        }
        var layer = DrawingInput.Present(p, "layer") ? Definition(d.StylesManager.Layers.Cast<Layer>(), p.Value<string>("layer"), null, x => x.Name) : null;
        var selection = d.SelectSet.Cast<object>().ToArray();
        var outcome = DrawingSupport.Atomic(ctx, d, "Create drawing sketch", () =>
        {
            var sketch = sketches.Add(); sketch.Name = name;
            var local = p.Value<string>("space") == "view_local";
            Point2d Map(JToken token) { var point = DrawingSupport.Point(app, token); return local ? point : sketch.SheetToSketchSpace(point); }
            double Radius(JToken center, double radiusMm) { var x = new JArray(center[0]!.Value<double>() + radiusMm, center[1]!.Value<double>()); return Map(center).DistanceTo(Map(x)); }
            double Angle(JToken center, double degrees)
            {
                var radians = (degrees % 360) * Math.PI / 180; var c = Map(center);
                var direction = Map(new JArray(center[0]!.Value<double>() + Math.Cos(radians), center[1]!.Value<double>() + Math.Sin(radians)));
                return Math.Atan2(direction.Y - c.Y, direction.X - c.X);
            }
            var origin = Map(new JArray(0, 0)); var xAxis = Map(new JArray(1, 0)); var yAxis = Map(new JArray(0, 1));
            var handedness = Math.Sign((xAxis.X - origin.X) * (yAxis.Y - origin.Y) - (xAxis.Y - origin.Y) * (yAxis.X - origin.X));
            if (handedness == 0) throw new ArgumentException("Sketch coordinate transform is singular.");
            var checks = new List<Action>(); var editing = false;
            try
            {
                sketch.Edit(); editing = true;
                foreach (JObject entity in (JArray)p["entities"]!)
                {
                    var property = entity.Properties().Single(); var e = (JObject)property.Value;
                    switch (property.Name)
                    {
                        case "line":
                            var from = Map(e["from"]!); var to = Map(e["to"]!); var line = sketch.SketchLines.AddByTwoPoints(from, to);
                            if (layer != null) line.Layer = layer;
                            if (p["color_rgb"] is JArray lineColor) line.OverrideColor = app.TransientObjects.CreateColor(lineColor[0].Value<byte>(), lineColor[1].Value<byte>(), lineColor[2].Value<byte>());
                            if (DrawingInput.Present(p, "weight_mm")) line.LineWeight = p.Value<double>("weight_mm") / 10;
                            checks.Add(() => { CheckPoint(line.StartSketchPoint.Geometry, from); CheckPoint(line.EndSketchPoint.Geometry, to); });
                            break;
                        case "circle":
                            var center = Map(e["center"]!); var radius = Radius(e["center"]!, e.Value<double>("radius_mm")); var circle = sketch.SketchCircles.AddByCenterRadius(center, radius);
                            if (layer != null) circle.Layer = layer;
                            if (p["color_rgb"] is JArray circleColor) circle.OverrideColor = app.TransientObjects.CreateColor(circleColor[0].Value<byte>(), circleColor[1].Value<byte>(), circleColor[2].Value<byte>());
                            if (DrawingInput.Present(p, "weight_mm")) circle.LineWeight = p.Value<double>("weight_mm") / 10;
                            checks.Add(() => { CheckPoint(circle.CenterSketchPoint.Geometry, center); CheckLength(circle.Geometry.Radius, radius); });
                            break;
                        case "arc":
                            var arcCenter = Map(e["center"]!); var arcRadius = Radius(e["center"]!, e.Value<double>("radius_mm")); var start = Angle(e["center"]!, e.Value<double>("start_deg")); var sweep = e.Value<double>("sweep_deg") * Math.PI / 180 * handedness;
                            // Native creation uses a counter-clockwise sweep. Reverse a clockwise request without approximating it.
                            var nativeStart = sweep < 0 ? start + sweep : start;
                            var arc = sketch.SketchArcs.AddByCenterStartSweepAngle(arcCenter, arcRadius, nativeStart, Math.Abs(sweep));
                            if (layer != null) arc.Layer = layer;
                            if (p["color_rgb"] is JArray arcColor) arc.OverrideColor = app.TransientObjects.CreateColor(arcColor[0].Value<byte>(), arcColor[1].Value<byte>(), arcColor[2].Value<byte>());
                            if (DrawingInput.Present(p, "weight_mm")) arc.LineWeight = p.Value<double>("weight_mm") / 10;
                            checks.Add(() => { CheckPoint(arc.CenterSketchPoint.Geometry, arcCenter); CheckLength(arc.Geometry.Radius, arcRadius); CheckLength(Math.Abs(arc.Geometry.SweepAngle), Math.Abs(sweep)); CheckPoint(arc.StartSketchPoint.Geometry, app.TransientGeometry.CreatePoint2d(arcCenter.X + arcRadius * Math.Cos(nativeStart), arcCenter.Y + arcRadius * Math.Sin(nativeStart))); CheckPoint(arc.EndSketchPoint.Geometry, app.TransientGeometry.CreatePoint2d(arcCenter.X + arcRadius * Math.Cos(nativeStart + Math.Abs(sweep)), arcCenter.Y + arcRadius * Math.Sin(nativeStart + Math.Abs(sweep)))); });
                            break;
                        case "text":
                            var position = Map(e["position"]!); var text = sketch.TextBoxes.AddFitted(position, SecurityElement.Escape(e.Value<string>("text")) ?? "");
                            if (layer != null) text.Layer = layer;
                            text.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextLeft; text.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextUpper;
                            if (DrawingInput.Present(e, "font_size_mm"))
                            {
                                var style = (TextStyle)text.Style.Copy("BimwrightSketchText_" + Guid.NewGuid().ToString("N")); style.FontSize = Radius(e["position"]!, e.Value<double>("font_size_mm")); text.Style = style;
                            }
                            var rotation = Angle(e["position"]!, e.Value<double?>("rotation_deg") ?? 0);
                            if (Math.Abs(rotation) > 1e-10)
                            {
                                text.ShowBoundaries = true;
                                var objects = app.TransientObjects.CreateObjectCollection(); objects.Add(text);
                                sketch.RotateSketchObjects(objects, position, rotation, false, false);
                                text.ShowBoundaries = false;
                            }
                            if (p["color_rgb"] is JArray color) text.Color = app.TransientObjects.CreateColor(color[0].Value<byte>(), color[1].Value<byte>(), color[2].Value<byte>());
                            checks.Add(() => { CheckPoint(text.Origin, position); if (text.Text != e.Value<string>("text")) throw new ArgumentException("Literal sketch text did not match native readback."); CheckLength(Math.Sin(text.Rotation - rotation), 0); CheckLength(Math.Cos(text.Rotation - rotation), 1); if (DrawingInput.Present(e, "font_size_mm")) CheckLength(text.Style.FontSize, Radius(e["position"]!, e.Value<double>("font_size_mm"))); });
                            break;
                    }
                }
            }
            finally
            {
                try { if (editing) sketch.ExitEdit(); }
                finally
                {
                    d.SelectSet.Clear();
                    if (selection.Length > 0) { var selected = app.TransientObjects.CreateObjectCollection(); foreach (var item in selection) selected.Add(item); d.SelectSet.SelectMultiple(selected); }
                }
            }
            d.Update(); foreach (var check in checks) check();
            var native = SketchContent(sketch);
            if (((JArray)native["entities"]!).Count != ((JArray)p["entities"]!).Count) throw new ArgumentException("Sketch entity count did not match readback.");
            if (DrawingInput.Present(p, "weight_mm") && ((JArray)native["curve_attributes"]!).Any(row => Math.Abs(row.Value<double>("weight_mm") - p.Value<double>("weight_mm")) > 1e-6)) throw new ArgumentException("Sketch line weight did not match readback.");
            if (p["color_rgb"] is JArray expectedColor && (((JArray)native["curve_attributes"]!).Any(row => !JToken.DeepEquals(row["color"], expectedColor)) || ((JArray)native["entities"]!).Any(row => row.Value<string>("kind") == "text" && !JToken.DeepEquals(row["color_rgb"], expectedColor)))) throw new ArgumentException("Sketch color did not match readback.");
            if (layer != null && ((JArray)native["entities"]!).Any(row => row.Value<string>("layer") != layer.Name)) throw new ArgumentException("Sketch layer did not match readback.");
            DrawingSupport.Mark(sketch.AttributeSets, name, p); DrawingSupport.Write(sketch.AttributeSets, "native_hash", DrawingGeometry.Hash(native.ToString(Formatting.None)));
            return new JObject { ["created"] = true, ["created_count"] = 1, ["name"] = sketch.Name, ["sheet"] = sheet.Name, ["view"] = view?.Name, ["space"] = p.Value<string>("space"), ["entity_count"] = checks.Count, ["follows_view"] = view != null, ["box_mm"] = SketchBox(sketch), ["content"] = native, ["ui_restored"] = true, ["saved"] = false };
        });
        if (outcome.Data is JObject data)
        {
            var restored = false;
            try
            {
                // Committing/aborting a native transaction may clear the restored selection.
                d.SelectSet.Clear();
                if (selection.Length > 0) { var selected = app.TransientObjects.CreateObjectCollection(); foreach (var item in selection) selected.Add(item); d.SelectSet.SelectMultiple(selected); }
                restored = app.ActiveDocument != null && app.ActiveDocument.Equals(d) && d.ActiveSheet.Equals(sheet) && sheet.Equals(app.ActiveEditObject) && d.SelectSet.Cast<object>().SequenceEqual(selection);
            }
            catch (Exception ex) { data["ui_restore_error"] = ex.Message; }
            data["ui_restored"] = restored;
            if (!restored)
            {
                data["readback_required"] = true;
                if (data.Value<bool?>("ok") != false) { data["ok"] = false; data["mutation_applied"] = true; data["error"] = new JObject { ["code"] = InventorErrorCodes.API_ERROR, ["message"] = "Sketch was created but UI/selection restoration needs readback. Do not replay the write." }; }
            }
        }
        return outcome;
    }

    private static void CheckPoint(Point2d actual, Point2d expected) { if (actual.DistanceTo(expected) > 1e-6) throw new ArgumentException("Sketch coordinates did not match native readback."); }
    private static void CheckLength(double actual, double expected) { if (Math.Abs(actual - expected) > 1e-6) throw new ArgumentException("Sketch size/angle did not match native readback."); }

    private static InventorCommandResult HideEdges(InventorCommandContext ctx, DrawingDocument d, Sheet sheet, JObject p)
    {
        var timer = Stopwatch.StartNew(); var view = DrawingSupport.View(sheet, p.Value<string>("view"));
        var snapshot = DrawingGeometry.Read(d, sheet, view);
        HashSet<DrawingCurve>? occurrenceCurves = null;
        if (DrawingInput.Present(p, "occurrences"))
        {
            if (view.ReferencedDocumentDescriptor.ReferencedDocument is not AssemblyDocument assembly) throw new ArgumentException("occurrences requires an assembly-backed drawing view.");
            if (!OccurrenceSelection.TryResolve(assembly.ComponentDefinition, p["occurrences"], "occurrences", out var selected, out var error, defaultLimit: 500)) throw new ArgumentException(error);
            occurrenceCurves = new HashSet<DrawingCurve>(selected.Where(item => !item.Suppressed && view.GetVisibility(item.Occurrence)).SelectMany(item => view.DrawingCurves[item.Occurrence].Cast<DrawingCurve>()));
        }
        var all = new List<(DrawingCurveSegment segment, bool visible, string id)>();
        var candidates = new HashSet<DrawingCurveSegment>();
        var threshold = p.Value<double?>("max_model_size_mm");
        if (view.Scale <= 0) throw new ArgumentException("View scale must be positive.");
        for (var i = 0; i < snapshot.Curves.Length; i++)
        {
            var curve = snapshot.Curves[i]; var chosen = (occurrenceCurves == null || occurrenceCurves.Contains(curve)) && (!threshold.HasValue || snapshot.Rows[i].Value<double>("projected_length_mm") / view.Scale <= threshold.Value);
            var j = 0;
            foreach (DrawingCurveSegment segment in curve.Segments)
            {
                var visible = segment.Visible; all.Add((segment, visible, "curve:" + (i + 1) + "/segment:" + (++j)));
                if (all.Count > 20000) throw new ArgumentException("View exceeds the 20000-segment visibility readback limit.");
                if (chosen && visible) candidates.Add(segment);
            }
        }
        var enumerationMs = timer.Elapsed.TotalMilliseconds;
        JObject Result(bool dryRun, int hidden, string revision, double applyMs, double readbackMs) => new JObject {
            ["updated"] = !dryRun && hidden > 0, ["dry_run"] = dryRun, ["document"] = d.DisplayName, ["sheet"] = sheet.Name, ["view"] = view.Name,
            ["candidate_count"] = candidates.Count, ["hidden_count"] = hidden, ["revision"] = revision, ["document_unchanged"] = dryRun || hidden == 0,
            ["criteria"] = new JObject { ["occurrences"] = p["occurrences"]?.DeepClone(), ["max_model_size_mm"] = threshold, ["combination"] = "and", ["size_measure"] = "projected_curve_length_mm / view_scale" },
            ["items"] = new JArray(all.Where(item => candidates.Contains(item.segment)).Select(item => new JObject { ["geometry_id"] = item.id.Split('/')[0], ["segment_id"] = item.id, ["visible"] = dryRun })),
            ["timings_ms"] = new JObject { ["enumerate"] = enumerationMs, ["apply"] = applyMs, ["readback"] = readbackMs, ["total"] = timer.Elapsed.TotalMilliseconds }, ["saved"] = false };
        if (p.Value<bool?>("dry_run") == true || candidates.Count == 0) return DrawingSupport.Success(ctx, Result(p.Value<bool?>("dry_run") == true, 0, snapshot.Revision, 0, 0));
        return DrawingSupport.Atomic(ctx, d, "Hide drawing view edges", () =>
        {
            var applyStart = timer.Elapsed.TotalMilliseconds;
            foreach (var segment in candidates) segment.Visible = false;
            var applyMs = timer.Elapsed.TotalMilliseconds - applyStart; var readbackStart = timer.Elapsed.TotalMilliseconds;
            d.Update();
            if (all.Any(item => item.segment.Visible != (candidates.Contains(item.segment) ? false : item.visible))) throw new ArgumentException("Segment visibility readback failed; the visibility transaction will be aborted.");
            var revision = DrawingGeometry.Read(d, sheet, view).Revision;
            return Result(false, candidates.Count, revision, applyMs, timer.Elapsed.TotalMilliseconds - readbackStart);
        });
    }
}
#endif
