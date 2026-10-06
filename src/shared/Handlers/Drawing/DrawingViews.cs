#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    private static void ViewOptions(DrawingView v, JObject p)
    {
        if (DrawingInput.Present(p, "label")) { v.ShowLabel = true; v.Label.FormattedText = (string)p["label"]!; }
        if (DrawingInput.Present(p, "design_view")) v.SetDesignViewRepresentation((string)p["design_view"]!, p.Value<bool?>("design_view_associative") ?? true);
        if (DrawingInput.Present(p, "reference_display")) v.ReferenceDataDisplayStyle = (string)p["reference_display"]! switch
        {
            "as_reference_shaded" => ReferenceDataDisplayStyleEnum.kEdgesAsReferenceShaded,
            "as_part_shaded" => ReferenceDataDisplayStyleEnum.kEdgesAsPartShaded,
            "as_reference" => ReferenceDataDisplayStyleEnum.kEdgesAsReference,
            "as_part" => ReferenceDataDisplayStyleEnum.kEdgesAsPart,
            "off" => ReferenceDataDisplayStyleEnum.kDisplayStyleOff,
            _ => throw new ArgumentException("reference_display must be as_reference_shaded|as_part_shaded|as_reference|as_part|off.")
        };
        if (DrawingInput.Present(p, "margin_mm")) v.Margin = p.Value<double>("margin_mm") / 10;
        if (DrawingInput.Present(p, "hidden_line_all_bodies")) v.HiddenLineCalculationForAllBodies = p.Value<bool>("hidden_line_all_bodies");
    }
    private static Inventor.Document Model(InventorCommandContext ctx, JObject p)
    {
        var document = ActiveDocumentSupport.ResolveTarget(ctx, new JObject { ["document"] = p["model"] }, out var error);
        if (error != null || document == null) throw new ArgumentException(error?.Error?.Message ?? "Model must already be loaded.");
        if (document.DocumentType != DocumentTypeEnum.kPartDocumentObject && document.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject) throw new ArgumentException("View model must be a part or assembly.");
        if (document.HasReferencesMissing) throw new ArgumentException("Model has missing references.");
        if (DrawingInput.Present(p, "design_view"))
        {
            if (document is not AssemblyDocument assembly) throw new ArgumentException("design_view requires an assembly model.");
            if (!assembly.ComponentDefinition.RepresentationsManager.DesignViewRepresentations.Cast<DesignViewRepresentation>().Any(x => x.Name == (string)p["design_view"]!)) throw new ArgumentException("Design view does not exist in the model.");
        }
        return document;
    }
    private static void PreflightView(InventorCommandContext ctx, Sheet s, JObject p)
    {
        var kind = (string)p["kind"]!;
        if (kind == "base" || kind == "arbitrary") { Model(ctx, p); if (DrawingInput.Present(p, "parent_view") || DrawingInput.Present(p, "detail_region_mm")) throw new ArgumentException("Parent/detail fields are irrelevant for base/arbitrary views."); }
        else { DrawingSupport.View(s, (string?)p["parent_view"]); if (DrawingInput.Present(p, "model") || DrawingInput.Present(p, "design_view")) throw new ArgumentException("model/design_view are inherited by projected/detail views."); }
        if (kind != "arbitrary" && (DrawingInput.Present(p, "eye_direction") || DrawingInput.Present(p, "up_direction"))) throw new ArgumentException("Camera vectors require kind=arbitrary.");
        if (kind == "arbitrary") Camera(DrawingSupport.App(ctx), p);
        if (kind == "detail") { var region = p["detail_region_mm"] as JObject ?? throw new ArgumentException("detail_region_mm must be an object."); DrawingInput.Required(region, "center", "radius_mm"); DrawingInput.Point(region["center"], 2); DrawingInput.Positive(region, "radius_mm"); }
        if (DrawingInput.Present(p, "reference_display")) DrawingInput.Choice(p, "reference_display", "as_reference_shaded", "as_part_shaded", "as_reference", "as_part", "off");
        if (kind == "base") Orientation((string?)p["orientation"] ?? "front");
    }
    private static ViewOrientationTypeEnum Orientation(string name) => name switch { "front" => ViewOrientationTypeEnum.kFrontViewOrientation, "back" => ViewOrientationTypeEnum.kBackViewOrientation, "top" => ViewOrientationTypeEnum.kTopViewOrientation, "bottom" => ViewOrientationTypeEnum.kBottomViewOrientation, "left" => ViewOrientationTypeEnum.kLeftViewOrientation, "right" => ViewOrientationTypeEnum.kRightViewOrientation, "iso_top_right" => ViewOrientationTypeEnum.kIsoTopRightViewOrientation, _ => throw new ArgumentException("Unknown model orientation.") };
    private static Camera Camera(Application app, JObject p)
    {
        var e = (JArray)p["eye_direction"]!; var up = p["up_direction"] ?? new JArray(0, 1, 0); DrawingInput.Point(up, 3);
        var x = (double)e[0]; var y = (double)e[1]; var z = (double)e[2]; var ux = (double)up[0]!; var uy = (double)up[1]!; var uz = (double)up[2]!;
        var n = Math.Sqrt(x * x + y * y + z * z); var cross = Math.Sqrt(Math.Pow(y * uz - z * uy, 2) + Math.Pow(z * ux - x * uz, 2) + Math.Pow(x * uy - y * ux, 2));
        if (n < 1e-9 || cross < 1e-9) throw new ArgumentException("Eye/up vectors must be nonzero and nonparallel.");
        var c = app.TransientObjects.CreateCamera(); c.Target = app.TransientGeometry.CreatePoint(0, 0, 0); c.Eye = app.TransientGeometry.CreatePoint(x / n * 100, y / n * 100, z / n * 100); c.UpVector = app.TransientGeometry.CreateUnitVector(ux, uy, uz); return c;
    }
    private static DrawingView CreateView(InventorCommandContext ctx, Sheet s, JObject p)
    {
        var app = DrawingSupport.App(ctx); var position = DrawingSupport.Point(app, p["position_mm"]); var style = DrawingSupport.Style((string?)p["style"]); var scale = p.Value<double?>("scale") ?? 1;
        DrawingView v;
        switch ((string)p["kind"]!)
        {
            case "base":
            case "arbitrary":
                var options = app.TransientObjects.CreateNameValueMap();
                if (DrawingInput.Present(p, "design_view")) { options.Add("DesignViewRepresentation", (string)p["design_view"]!); options.Add("DesignViewAssociative", p.Value<bool?>("design_view_associative") ?? true); }
                var arbitrary = (string)p["kind"]! == "arbitrary";
                v = s.DrawingViews.AddBaseView((Inventor._Document)(object)Model(ctx, p), position, scale, arbitrary ? ViewOrientationTypeEnum.kArbitraryViewOrientation : Orientation((string?)p["orientation"] ?? "front"), style, ArbitraryCamera: arbitrary ? Camera(app, p) : Type.Missing, AdditionalOptions: options); break;
            case "projected": v = s.DrawingViews.AddProjectedView(DrawingSupport.View(s, (string?)p["parent_view"]), position, style, scale); break;
            case "detail":
                var region = (JObject)p["detail_region_mm"]!;
                v = (DrawingView)(object)s.DrawingViews.AddDetailView(DrawingSupport.View(s, (string?)p["parent_view"]), position, style, true, DrawingSupport.Point(app, region["center"]), (double)region["radius_mm"]! / 10, Scale: scale, Name: (string)p["name"]!); break;
            default: throw new ArgumentException("Unknown view kind.");
        }
        if (v.Position.DistanceTo(position) > 1e-7) { if (v.Aligned) v.Aligned = false; v.Position = position; }
        v.Name = (string)p["name"]!; ViewOptions(v, p); DrawingSupport.Mark(v.AttributeSets, v.Name, p); return v;
    }
    private static InventorCommandResult AddView(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var name = (string)p["name"]!; var found = s.DrawingViews.Cast<DrawingView>().SingleOrDefault(v => v.Name == name);
        if (found != null) { DrawingSupport.ExistingView(found, p); var existing = DrawingSupport.ViewInfo(found); existing["created"] = false; return DrawingSupport.Success(ctx, existing); }
        PreflightView(ctx, s, p);
        return DrawingSupport.Atomic(ctx, d, "Add drawing view", () => { var v = CreateView(ctx, s, p); d.Update(); var row = DrawingSupport.ViewInfo(v); row["created"] = true; return row; });
    }
    private static InventorCommandResult AddSection(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var name = (string)p["name"]!; var parent = DrawingSupport.View(s, (string?)p["parent_view"]); var existing = s.DrawingViews.Cast<DrawingView>().SingleOrDefault(v => v.Name == name);
        if (existing != null) { DrawingSupport.ExistingView(existing, p); var row = DrawingSupport.ViewInfo(existing); row["created"] = false; return DrawingSupport.Success(ctx, row); }
        var inherit = p["inherit_3d"] as JObject;
        if (inherit != null) { DrawingInput.Required(inherit, "name", "position_mm"); DrawingInput.Point(inherit["position_mm"], 2); if ((string)inherit["name"]! == name || s.DrawingViews.Cast<DrawingView>().Any(v => v.Name == (string)inherit["name"]!)) throw new ArgumentException("Inherited view name conflicts."); }
        var app = DrawingSupport.App(ctx);
        return DrawingSupport.Atomic(ctx, d, "Add section view", () =>
        {
            var sketch = parent.Sketches.Add();
            sketch.Edit(); try { sketch.SketchLines.AddByTwoPoints(sketch.SheetToSketchSpace(DrawingSupport.Point(app, p["cut_line_mm"]![0])), sketch.SheetToSketchSpace(DrawingSupport.Point(app, p["cut_line_mm"]![1]))); } finally { sketch.ExitEdit(); }
            var section = s.DrawingViews.AddSectionView(parent, sketch, DrawingSupport.Point(app, p["position_mm"]), DrawingInput.Present(p, "style") ? DrawingSupport.Style((string?)p["style"]) : parent.ViewStyle, Scale: (object?)p.Value<double?>("scale") ?? Type.Missing, Name: name, FullDepth: !DrawingInput.Present(p, "depth_mm"), SectionDepth: DrawingInput.Present(p, "depth_mm") ? (object)(p.Value<double>("depth_mm") / 10) : Type.Missing);
            if ((string?)p["direction"] == "negative") section.ReverseDirection(); section.Rotation = p.Value<double?>("rotation_deg") * Math.PI / 180 ?? 0;
            if (section.Aligned) section.Aligned = false;
            section.Position = DrawingSupport.Point(app, p["position_mm"]);
            var view = (DrawingView)(object)section; ViewOptions(view, p); DrawingSupport.Mark(view.AttributeSets, name, p);
            d.Update();
            var row = DrawingSupport.ViewInfo(view); row["created"] = true; row["cut_line_mm"] = p["cut_line_mm"]!.DeepClone(); row["depth_mm"] = section.FullSectionDepth ? JValue.CreateNull() : new JValue(section.SectionDepth * 10); row["direction"] = (string?)p["direction"] ?? "positive";
            if (inherit != null) { var inherited = s.DrawingViews.AddProjectedView(view, DrawingSupport.Point(app, inherit["position_mm"]), view.ViewStyle, view.Scale); inherited.Name = (string)inherit["name"]!; DrawingSupport.Mark(inherited.AttributeSets, inherited.Name, inherit); row["inherited_view"] = DrawingSupport.ViewInfo(inherited); }
            d.Update(); return row;
        });
    }
    private static InventorCommandResult EditView(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var v = DrawingSupport.View(s, (string?)p["view"]); var app = DrawingSupport.App(ctx);
        var children = s.DrawingViews.Cast<DrawingView>().Where(x => x.ParentView != null && x.ParentView.Name == v.Name).Select(x => x.Name).ToArray();
        if (DrawingInput.Present(p, "crop")) throw new NotSupportedException("The 2027 interop exposes no drawing crop API. Crop is blocked pending a verified implementation.");
        var shaded = v.ViewStyle == DrawingViewStyleEnum.kShadedDrawingViewStyle || v.ViewStyle == DrawingViewStyleEnum.kShadedHiddenLineDrawingViewStyle;
        var moving = DrawingInput.Present(p, "position_mm") || DrawingInput.Present(p, "align");
        var rebuild = shaded && moving;
        if (rebuild && p.Value<bool?>("rebuild") != true) throw new ArgumentException("Moving a shaded view requires rebuild=true to avoid shifted export shading.");
        if (rebuild) DrawingSupport.RequireAnnotations(s);
        if (rebuild && (children.Length > 0 || s.DrawingDimensions.GeneralDimensions.Count > 0 || s.SketchedSymbols.Count > 0 || s.Balloons.Count > 0)) throw new ArgumentException("Cannot rebuild a shaded view while dependent or unmanaged annotations/views may be lost.");
        var position = DrawingInput.Present(p, "position_mm") ? DrawingSupport.Point(app, p["position_mm"]) : v.Position;
        if (p["align"] is JObject align)
        {
            if (DrawingInput.Present(p, "position_mm")) throw new ArgumentException("align conflicts with explicit position_mm."); DrawingInput.Required(align, "axis", "with_view"); DrawingInput.Choice(align, "axis", "x", "y");
            var other = DrawingSupport.View(s, (string?)align["with_view"]); if (other.Name == v.Name) throw new ArgumentException("Cannot align a view to itself.");
            var offset = (align.Value<double?>("offset_mm") ?? 0) / 10; position = (string)align["axis"]! == "x" ? app.TransientGeometry.CreatePoint2d(other.Position.X + offset, position.Y) : app.TransientGeometry.CreatePoint2d(position.X, other.Position.Y + offset);
        }
        JObject? original = null;
        if (rebuild) { var signature = DrawingSupport.Read(v.AttributeSets, "signature"); if (signature == null) throw new ArgumentException("Only a managed view can be rebuilt safely."); original = JObject.Parse(signature); if ((string?)original["kind"] != "base" && (string?)original["kind"] != "arbitrary") throw new NotSupportedException("Shaded dependent-view rebuild is blocked pending dependency-preservation probes."); }
        if (DrawingInput.Present(p, "reference_display")) DrawingInput.Choice(p, "reference_display", "as_reference_shaded", "as_part_shaded", "as_reference", "as_part", "off");
        return DrawingSupport.Atomic(ctx, d, "Edit drawing view", () =>
        {
            if (rebuild) { original!["position_mm"] = new JArray(position.X * 10, position.Y * 10); foreach (var field in new[] { "scale", "style", "label", "design_view", "reference_display", "margin_mm", "hidden_line_all_bodies" }) if (DrawingInput.Present(p, field)) original[field] = p[field]!.DeepClone(); v.Delete(); v = CreateView(ctx, s, original); }
            else if (moving) { if (v.Aligned) v.Aligned = false; v.Position = position; }
            if (DrawingInput.Present(p, "scale")) { if (v.ParentView != null) v.ScaleFromBase = false; v.Scale = p.Value<double>("scale"); }
            if (DrawingInput.Present(p, "style")) v.ViewStyle = DrawingSupport.Style((string?)p["style"]);
            if (DrawingInput.Present(p, "suppressed")) v.Suppressed = p.Value<bool>("suppressed");
            if (DrawingInput.Present(p, "rotation_deg")) v.Rotation = p.Value<double>("rotation_deg") * Math.PI / 180;
            ViewOptions(v, p); d.Update(); var row = DrawingSupport.ViewInfo(v); row["updated"] = true; row["rebuilt"] = rebuild; row["dependent_views"] = new JArray(children); return row;
        });
    }
}
#endif
