#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    private static IEnumerable<JObject> FlattenDimensions(JArray items)
    {
        foreach (JObject item in items)
        {
            if ((string?)item["kind"] != "chain") { yield return item; continue; }
            var positions = (JArray)item["text_positions_mm"]!; var intents = (JArray)item["intents"]!;
            for (var i = 0; i < positions.Count; i++) { var flat = (JObject)item.DeepClone(); flat["name"] = (string)item["name"]! + ":" + (i + 1); flat["kind"] = (string?)item["direction"] ?? "aligned"; flat["intents"] = new JArray(intents[i].DeepClone(), intents[i + 1].DeepClone()); flat["text_position_mm"] = positions[i].DeepClone(); flat.Remove("text_positions_mm"); flat.Remove("direction"); yield return flat; }
        }
    }
    private static GeneralDimension? DimensionByName(Sheet sheet, string name) => sheet.DrawingDimensions.GeneralDimensions.Cast<GeneralDimension>().SingleOrDefault(x => DrawingSupport.Read(x.AttributeSets, "name") == name);
    private static void AnnotationConflict(Sheet s, string name) { if (s.SketchedSymbols.Cast<SketchedSymbol>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) || s.Balloons.Cast<Balloon>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name)) throw new ArgumentException("Annotation name conflicts: " + name); }
    private sealed class DimensionPlan
    {
        internal JObject Input { get; init; } = null!; internal DrawingIntent[] Intents { get; init; } = null!; internal GeneralDimension? Existing { get; init; }
        internal DimensionStyle? Style { get; init; }
    }
    private static JObject DimensionInfo(GeneralDimension dimension, JObject item, bool created) => new JObject { ["name"] = (string)item["name"]!, ["view"] = (string)item["view"]!, ["kind"] = (string)item["kind"]!, ["value"] = (string?)item["kind"] == "angular" ? dimension.ModelValue * 180 / Math.PI : dimension.ModelValue * 10, ["unit"] = (string?)item["kind"] == "angular" ? "deg" : "mm", ["attached"] = dimension.Attached, ["created"] = created, ["text_position_mm"] = new JArray(dimension.Text.Origin.X * 10, dimension.Text.Origin.Y * 10), ["text"] = dimension.Text.FormattedText, ["intents"] = item["intents"]!.DeepClone(), ["precision"] = dimension.Precision };
    private static InventorCommandResult AddDimensions(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        DrawingSupport.RequireAnnotations(s);
        var plans = new List<DimensionPlan>();
        foreach (var item in FlattenDimensions((JArray)p["items"]!))
        {
            var name = (string)item["name"]!; AnnotationConflict(s, name); var existing = DimensionByName(s, name);
            if (existing != null) { DrawingSupport.Existing(existing.AttributeSets, item); plans.Add(new DimensionPlan { Input = item, Existing = existing, Intents = Array.Empty<DrawingIntent>() }); continue; }
            var view = DrawingSupport.View(s, (string?)item["view"]); var intents = ((JArray)item["intents"]!).Cast<JObject>().Select(x => DrawingGeometry.Resolve(s, view, x, item.Value<double?>("tolerance_mm") ?? 0.5)).ToArray();
            var kind = (string)item["kind"]!;
            if (kind == "horizontal" || kind == "vertical" || kind == "aligned")
            {
                var dx = Math.Abs(intents[0].Point.X - intents[1].Point.X); var dy = Math.Abs(intents[0].Point.Y - intents[1].Point.Y);
                if (kind == "horizontal" && dx < 1e-7 || kind == "vertical" && dy < 1e-7 || kind == "aligned" && Math.Sqrt(dx * dx + dy * dy) < 1e-7) throw new ArgumentException("Dimension '" + name + "' has coincident projected intents for the requested direction.");
            }
            if ((kind == "diameter" || kind == "radius") && intents[0].Curve.CurveType != CurveTypeEnum.kCircleCurve && intents[0].Curve.CurveType != CurveTypeEnum.kCircularArcCurve) throw new ArgumentException(kind + " requires circular geometry.");
            if (kind == "angular" && intents.Any(x => x.Curve.CurveType != CurveTypeEnum.kLineSegmentCurve)) throw new ArgumentException("Angular dimensions require two line curves.");
            DimensionStyle? style = null;
            if (DrawingInput.Present(item, "style")) style = Definition(d.StylesManager.DimensionStyles.Cast<DimensionStyle>(), (string?)item["style"], null, x => x.Name);
            plans.Add(new DimensionPlan { Input = item, Intents = intents, Style = style });
        }
        return DrawingSupport.Atomic(ctx, d, "Add drawing dimensions", () =>
        {
            var result = new JArray(); var created = 0;
            foreach (var plan in plans)
            {
                var item = plan.Input; if (plan.Existing != null) { result.Add(DimensionInfo(plan.Existing, item, false)); continue; }
                var position = DrawingSupport.Point(DrawingSupport.App(ctx), item["text_position_mm"]); var dimensions = s.DrawingDimensions.GeneralDimensions; var a = plan.Intents[0].Intent; var kind = (string)item["kind"]!;
                var style = (object?)plan.Style ?? Type.Missing;
                GeneralDimension dim = kind switch
                {
                    "diameter" => (GeneralDimension)(object)dimensions.AddDiameter(position, s.CreateGeometryIntent(plan.Intents[0].Curve), DimensionStyle: style),
                    "radius" => (GeneralDimension)(object)dimensions.AddRadius(position, s.CreateGeometryIntent(plan.Intents[0].Curve), DimensionStyle: style),
                    "angular" => (GeneralDimension)(object)dimensions.AddAngular(position, s.CreateGeometryIntent(plan.Intents[0].Curve), s.CreateGeometryIntent(plan.Intents[1].Curve), DimensionStyle: style),
                    _ => (GeneralDimension)(object)dimensions.AddLinear(position, a, plan.Intents[1].Intent, kind switch { "horizontal" => DimensionTypeEnum.kHorizontalDimensionType, "vertical" => DimensionTypeEnum.kVerticalDimensionType, _ => DimensionTypeEnum.kAlignedDimensionType }, DimensionStyle: style)
                };
                if (DrawingInput.Present(item, "precision")) dim.Precision = item.Value<int>("precision");
                if (DrawingInput.Present(item, "text_override")) { dim.HideValue = true; dim.Text.FormattedText = SecurityElement.Escape((string)item["text_override"]!) ?? ""; }
                DrawingSupport.Mark(dim.AttributeSets, (string)item["name"]!, item); DrawingSupport.Write(dim.AttributeSets, "view", (string)item["view"]!); if (!dim.Attached) throw new InvalidOperationException("Created dimension is not attached."); result.Add(DimensionInfo(dim, item, true)); created++;
            }
            return new JObject { ["ok"] = true, ["count"] = result.Count, ["created_count"] = created, ["items"] = result };
        });
    }
    private sealed class BalloonPlan
    {
        internal JObject Input { get; init; } = null!; internal JObject Signature { get; init; } = null!; internal SketchedSymbol? Existing { get; init; }
        internal DrawingIntent? Intent { get; init; }
        internal Point2d Position { get; init; } = null!; internal Point2d? Elbow { get; init; }
        internal string[] Prompts { get; init; } = null!;
    }
    private static InventorCommandResult AddBalloons(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        DrawingSupport.RequireAnnotations(s);
        var mode = (string?)p["mode"] ?? "symbol";
        if (mode == "bom")
        {
            if (p.Value<bool?>("allow_model_bom_change") != true) throw new ArgumentException("Native BOM balloons require allow_model_bom_change=true.");
            throw new NotSupportedException("Native BOM balloons are blocked until cross-document BOM rollback is verified. Use supplied sketched symbols.");
        }
        if (mode != "symbol") throw new ArgumentException("mode must be symbol|bom.");
        var definition = Definition(d.SketchedSymbolDefinitions.Cast<SketchedSymbolDefinition>(), (string?)p["symbol"], null, x => x.Name);
        var app = DrawingSupport.App(ctx); var layout = p["layout"] as JObject;
        if (layout != null) { DrawingInput.Required(layout, "column_x_mm", "start_y_mm", "spacing_mm", "leader_angle_deg"); DrawingInput.Positive(layout, "spacing_mm"); DrawingInput.Range(layout, "leader_angle_deg", 1, 179); }
        var plans = new List<BalloonPlan>(); var index = 0;
        foreach (JObject item in (JArray)p["items"]!)
        {
            var name = (string)item["name"]!; if (DimensionByName(s, name) != null) throw new ArgumentException("Annotation name conflicts: " + name);
            var signature = new JObject { ["item"] = item.DeepClone(), ["mode"] = mode, ["symbol"] = definition.Name, ["prompts"] = p["prompts"]?.DeepClone(), ["layout"] = layout?.DeepClone() };
            var existing = s.SketchedSymbols.Cast<SketchedSymbol>().SingleOrDefault(x => DrawingSupport.Read(x.AttributeSets, "name") == name);
            if (existing != null) { DrawingSupport.Existing(existing.AttributeSets, signature); plans.Add(new BalloonPlan { Input = item, Signature = signature, Existing = existing }); index++; continue; }
            if (layout != null && DrawingInput.Present(item, "position_mm")) throw new ArgumentException("layout conflicts with explicit positions.");
            var position = layout == null ? DrawingSupport.Point(app, item["position_mm"]) : app.TransientGeometry.CreatePoint2d(layout.Value<double>("column_x_mm") / 10, (layout.Value<double>("start_y_mm") - index * layout.Value<double>("spacing_mm")) / 10);
            var view = DrawingSupport.View(s, (string?)item["view"]); var path = (string)item["occurrence_path"]!;
            var locator = item["intent"] as JObject;
            DrawingIntent intent;
            if (locator != null) { locator = (JObject)locator.DeepClone(); if (DrawingInput.Present(locator, "occurrence_path") && (string?)locator["occurrence_path"] != path) throw new ArgumentException("Balloon intent occurrence conflicts."); locator["occurrence_path"] = path; intent = DrawingGeometry.Resolve(s, view, locator, item.Value<double?>("tolerance_mm") ?? 0.5); }
            else
            {
                var curves = DrawingGeometry.Curves(view, path).Cast<DrawingCurve>().Where(c => c.Segments.Cast<DrawingCurveSegment>().Any(x => x.Visible)).ToArray();
                var region = item["target_region_mm"] as JObject;
                if (region != null) { DrawingInput.Required(region, "min", "max"); DrawingInput.Point(region["min"], 2); DrawingInput.Point(region["max"], 2); if ((double)region["max"]![0]! <= (double)region["min"]![0]! || (double)region["max"]![1]! <= (double)region["min"]![1]!) throw new ArgumentException("target_region_mm must have positive width/height."); }
                var ranked = curves.Select(c => (curve: c, point: c.MidPoint)).Where(x => x.point != null && (region == null || x.point.X * 10 >= (double)region["min"]![0]! && x.point.X * 10 <= (double)region["max"]![0]! && x.point.Y * 10 >= (double)region["min"]![1]! && x.point.Y * 10 <= (double)region["max"]![1]!)).Select(x => (x.curve, x.point, distance: x.point.DistanceTo(position))).OrderBy(x => x.distance).ToArray();
                if (ranked.Length == 0 || ranked.Length > 1 && Math.Abs(ranked[0].distance - ranked[1].distance) < 1e-7) throw new ArgumentException("Automatic balloon attachment is missing/ambiguous; supply an exact intent.");
                intent = new DrawingIntent { Curve = ranked[0].curve, Point = ranked[0].point, Intent = s.CreateGeometryIntent(ranked[0].curve, PointIntentEnum.kMidPointIntent), Locator = new JObject { ["occurrence_path"] = path, ["automatic"] = true } };
            }
            var prompts = p["prompts"] is JObject map ? (JObject)map.DeepClone() : new JObject(); var labels = PromptBoxes(definition.Sketch).Select(PromptLabel).ToArray();
            if (labels.Length == 1 && !DrawingInput.Present(prompts, labels[0])) prompts[labels[0]] = (string)item["text"]!;
            var strings = Prompts(definition.Sketch, prompts);
            if (labels.Length == 0 && !string.IsNullOrEmpty((string?)item["text"])) throw new ArgumentException("Symbol has no prompted text; requested balloon text cannot be represented.");
            Point2d? elbow = null;
            if (layout != null)
            {
                var target = intent.Point; var dy = target.Y - position.Y; var angle = layout.Value<double>("leader_angle_deg") * Math.PI / 180; var dx = Math.Abs(dy / Math.Tan(angle)); var sign = target.X >= position.X ? 1 : -1;
                elbow = app.TransientGeometry.CreatePoint2d(target.X - sign * dx, position.Y); if ((elbow.X - position.X) * sign < 0) throw new ArgumentException("Leader angle puts the elbow behind the symbol; adjust layout.");
            }
            plans.Add(new BalloonPlan { Input = item, Signature = signature, Intent = intent, Position = position, Elbow = elbow, Prompts = strings }); index++;
        }
        return DrawingSupport.Atomic(ctx, d, "Add drawing symbol balloons", () =>
        {
            var rows = new JArray(); var created = 0;
            foreach (var plan in plans)
            {
                var symbol = plan.Existing;
                if (symbol == null)
                {
                    var points = app.TransientObjects.CreateObjectCollection(); points.Add(plan.Position);
                    if (plan.Elbow != null) points.Add(plan.Elbow);
                    points.Add(plan.Intent!.Intent); symbol = s.SketchedSymbols.AddWithLeader(definition, points, PromptStrings: plan.Prompts); DrawingSupport.Mark(symbol.AttributeSets, (string)plan.Input["name"]!, plan.Signature); DrawingSupport.Write(symbol.AttributeSets, "view", (string)plan.Input["view"]!); created++;
                }
                var attached = symbol.Leader.AllLeafNodes.Cast<LeaderNode>().Any(node => node.AttachedEntity?.Geometry is DrawingCurve curve && curve.Parent.Name == (string)plan.Input["view"]!);
                if (!attached) throw new InvalidOperationException("Balloon leader is not attached to the requested view.");
                var actualPrompts = new JObject(); foreach (var box in PromptBoxes(definition.Sketch)) actualPrompts[PromptLabel(box)] = symbol.GetResultText(box);
                rows.Add(new JObject { ["name"] = (string)plan.Input["name"]!, ["created"] = plan.Existing == null, ["view"] = (string)plan.Input["view"]!, ["occurrence_path"] = (string)plan.Input["occurrence_path"]!, ["text"] = actualPrompts.Count == 1 ? actualPrompts.Properties().First().Value.DeepClone() : null, ["prompts"] = actualPrompts, ["position_mm"] = new JArray(symbol.Position.X * 10, symbol.Position.Y * 10), ["mode"] = "symbol", ["attached"] = attached, ["definition"] = definition.Name, ["model_bom_changed"] = false });
            }
            return new JObject { ["ok"] = true, ["count"] = rows.Count, ["created_count"] = created, ["items"] = rows, ["model_bom_changed"] = false };
        });
    }
}
#endif
