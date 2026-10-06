#if INVENTOR2027
using System;
using System.Linq;
using System.Security;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    private static ArrowheadTypeEnum Arrowhead(string? value) => value switch { "open" => ArrowheadTypeEnum.kOpenArrowheadType, "closed" => ArrowheadTypeEnum.kClosedArrowheadType, "filled" => ArrowheadTypeEnum.kFilledArrowheadType, "none" => ArrowheadTypeEnum.kNoneArrowheadType, _ => throw new ArgumentException("Unknown arrowhead.") };
    private static string ArrowStyleName(DrawingItem item, string source, ArrowheadTypeEnum arrow)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();
        var bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes((item.Name ?? item.Key) + ":" + source + ":" + arrow));
        return "BimwrightArrow_" + BitConverter.ToString(bytes).Replace("-", "").Substring(0, 24);
    }
    private static LeaderStyle SymbolArrowStyle(DrawingDocument d, DrawingItem item, LeaderStyle source, ArrowheadTypeEnum arrow)
    {
        if (source.ArrowheadType == arrow) return source;
        var name = ArrowStyleName(item, source.Name, arrow);
        var copy = d.StylesManager.LeaderStyles.Cast<LeaderStyle>().SingleOrDefault(x => x.Name == name) ?? (LeaderStyle)source.Copy(name);
        if (copy.ArrowheadType != arrow && copy.Name == name) copy.ArrowheadType = arrow;
        return copy;
    }
    private static DimensionStyle NoteArrowStyle(DrawingDocument d, DrawingItem item, DimensionStyle source, ArrowheadTypeEnum arrow)
    {
        if (source.LeaderStyle.ArrowheadType == arrow) return source;
        var name = ArrowStyleName(item, source.Name, arrow);
        var copy = d.StylesManager.DimensionStyles.Cast<DimensionStyle>().SingleOrDefault(x => x.Name == name) ?? (DimensionStyle)source.Copy(name);
        copy.LeaderStyle = SymbolArrowStyle(d, item, source.LeaderStyle, arrow);
        return copy;
    }
    private static object? AnnotationStyle(DrawingDocument d, DrawingItem item, JObject changes)
    {
        if (!DrawingInput.Present(changes, "style")) return null;
        var name = changes.Value<string>("style");
        return item.Entity switch { GeneralDimension or LeaderNote => Definition(d.StylesManager.DimensionStyles.Cast<DimensionStyle>(), name, null, x => x.Name), GeneralNote => Definition(d.StylesManager.TextStyles.Cast<TextStyle>(), name, null, x => x.Name), SketchedSymbol => Definition(d.StylesManager.LeaderStyles.Cast<LeaderStyle>(), name, null, x => x.Name), _ => throw new NotSupportedException("Native BOM balloon edits remain unverified; edit supplied symbol balloons.") };
    }
    private static SketchedSymbol RecreateSymbol(InventorCommandContext ctx, Sheet s, SketchedSymbol old, JToken position)
    {
        var points = DrawingSupport.App(ctx).TransientObjects.CreateObjectCollection(); points.Add(DrawingSupport.Point(DrawingSupport.App(ctx), position));
        var node = old.Leader.RootNode;
        while (true)
        {
            if (node.AttachedEntity != null) { points.Add(node.AttachedEntity); break; }
            if (node.ChildNodes.Count != 1) throw new NotSupportedException("Only a single unbranched attached symbol leader can be recreated.");
            node = node.ChildNodes[1]; if (node.AttachedEntity == null) points.Add(node.Position);
        }
        var strings = PromptBoxes(old.Definition.Sketch).Select(old.GetResultText).ToArray();
        var symbol = s.SketchedSymbols.AddWithLeader(old.Definition, points, old.Rotation, old.Scale, strings, old.Static, old.SymbolClipping);
        symbol.Layer = old.Layer; symbol.LeaderStyle = old.LeaderStyle; symbol.LeaderVisible = old.LeaderVisible; symbol.LeaderClipping = old.LeaderClipping;
        symbol.Color = old.Color; symbol.LineWeight = old.LineWeight; symbol.LineType = old.LineType;
        CopyDrawingAttributes(old.AttributeSets, symbol.AttributeSets);
        old.Delete(); return symbol;
    }
    private static InventorCommandResult EditAnnotations(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var inventory = Inventory(s);
        var plans = ((JArray)p["items"]!).Cast<JObject>().Select(target =>
        {
            var item = ResolveItem(d, s, inventory, target); var c = (JObject)target["changes"]!;
            if (item.Entity is Balloon) throw new NotSupportedException("Native BOM balloon edits remain unverified; edit supplied symbol balloons.");
            if (item.Entity is SketchedSymbol symbol)
            {
                if (DrawingInput.Present(c, "text_override") && PromptBoxes(symbol.Definition.Sketch).Length != 1) throw new ArgumentException("text_override requires a symbol with exactly one prompt.");
                if (DrawingInput.Present(c, "text_position_mm") && LeaderIntents(symbol.Leader).Any())
                {
                    if (p.Value<bool?>("rebuild") != true) throw new ArgumentException("Moving a leadered symbol requires rebuild=true.");
                    if (symbol.Leader.AllLeafNodes.Count != 1 || symbol.Leader.AllNodes.Cast<LeaderNode>().Any(n => n.ChildNodes.Count > 1)) throw new NotSupportedException("Only a single unbranched attached symbol leader can be recreated.");
                }
            }
            if (DrawingInput.Present(c, "leader_arrowhead") && (item.Entity is GeneralNote || !ItemIntents(item).Any())) throw new ArgumentException("leader_arrowhead requires an attached leader note or symbol.");
            var style = AnnotationStyle(d, item, c);
            var layer = DrawingInput.Present(c, "layer") ? Definition(d.StylesManager.Layers.Cast<Layer>(), c.Value<string>("layer"), null, x => x.Name) : null;
            var intents = ItemIntents(item).Select(i => (geometry: i.Geometry, type: i.IntentType, point: IntentPoint(i))).ToArray();
            var modelValue = item.Entity is GeneralDimension dimension ? (double?)dimension.ModelValue : null;
            return (item, c, style, layer, intents, modelValue);
        }).ToArray();
        if (plans.Select(x => x.item.Key).Distinct().Count() != plans.Length) throw new ArgumentException("Multiple selectors resolve to the same annotation.");
        return DrawingSupport.Atomic(ctx, d, "Edit drawing annotations", () =>
        {
            var rows = new JArray();
            foreach (var plan in plans)
            {
                var item = plan.item; var c = plan.c;
                if (item.Entity is SketchedSymbol old && DrawingInput.Present(c, "text_position_mm") && LeaderIntents(old.Leader).Any() && old.Position.DistanceTo(DrawingSupport.Point(DrawingSupport.App(ctx), c["text_position_mm"])) > 0.0001) item = new DrawingItem(RecreateSymbol(ctx, s, old, c["text_position_mm"]!), item.Kind);
                if (plan.layer != null) { switch (item.Entity) { case GeneralDimension x: x.Layer = plan.layer; break; case GeneralNote x: x.Layer = plan.layer; break; case LeaderNote x: x.Layer = plan.layer; break; case SketchedSymbol x: x.Layer = plan.layer; break; } }
                switch (item.Entity)
                {
                    case GeneralDimension x:
                        if (plan.style is DimensionStyle dimStyle) x.Style = dimStyle;
                        if (DrawingInput.Present(c, "precision")) x.Precision = c.Value<int>("precision");
                        if (DrawingInput.Present(c, "text_override")) { x.HideValue = true; x.Text.FormattedText = SecurityElement.Escape(c.Value<string>("text_override")); }
                        if (DrawingInput.Present(c, "text_position_mm")) x.Text.Origin = DrawingSupport.Point(DrawingSupport.App(ctx), c["text_position_mm"]);
                        break;
                    case GeneralNote x:
                        if (plan.style is TextStyle textStyle) x.TextStyle = textStyle;
                        if (DrawingInput.Present(c, "text_override")) x.Text = c.Value<string>("text_override");
                        if (DrawingInput.Present(c, "text_position_mm")) x.Position = DrawingSupport.Point(DrawingSupport.App(ctx), c["text_position_mm"]);
                        break;
                    case LeaderNote x:
                        if (DrawingInput.Present(c, "leader_arrowhead")) x.DimensionStyle = NoteArrowStyle(d, item, plan.style as DimensionStyle ?? x.DimensionStyle, Arrowhead(c.Value<string>("leader_arrowhead")));
                        else if (plan.style is DimensionStyle noteStyle) x.DimensionStyle = noteStyle;
                        if (DrawingInput.Present(c, "text_override")) x.Text = c.Value<string>("text_override");
                        if (DrawingInput.Present(c, "text_position_mm")) x.Leader.RootNode.Position = DrawingSupport.Point(DrawingSupport.App(ctx), c["text_position_mm"]);
                        break;
                    case SketchedSymbol x:
                        if (DrawingInput.Present(c, "leader_arrowhead")) x.LeaderStyle = SymbolArrowStyle(d, item, plan.style as LeaderStyle ?? x.LeaderStyle, Arrowhead(c.Value<string>("leader_arrowhead")));
                        else if (plan.style is LeaderStyle symbolStyle) x.LeaderStyle = symbolStyle;
                        if (DrawingInput.Present(c, "text_override")) x.SetPromptResultText(PromptBoxes(x.Definition.Sketch)[0], c.Value<string>("text_override"));
                        if (DrawingInput.Present(c, "text_position_mm") && !LeaderIntents(x.Leader).Any()) x.Position = DrawingSupport.Point(DrawingSupport.App(ctx), c["text_position_mm"]);
                        break;
                }
                d.Update();
                var after = ItemIntents(item).ToArray();
                // A circular dimension's parameter point follows the text angle; preserve
                // its curve and measured value rather than freezing that movable point.
                var circular = item.Entity is DiameterGeneralDimension || item.Entity is RadiusGeneralDimension;
                if (after.Length != plan.intents.Length || plan.intents.Any(i => !after.Any(a => a.Geometry.Equals(i.geometry) && a.IntentType == i.type && (circular || i.point == null || IntentPoint(a)?.DistanceTo(i.point) < 0.0001))) || item.Entity is GeneralDimension dim && (!dim.Attached || Math.Abs(dim.ModelValue - plan.modelValue!.Value) > 1e-9)) throw new InvalidOperationException("Annotation edit changed its native attachment or measured value.");
                if (item.Entity is GeneralDimension precisionDim && DrawingInput.Present(c, "precision") && precisionDim.Precision != c.Value<int>("precision")) throw new InvalidOperationException("Dimension precision readback differs from request.");
                var actual = ItemInfo(d, s, item); actual["updated"] = true; actual["rebuilt"] = !item.Entity.Equals(plan.item.Entity); rows.Add(actual);
                if (DrawingInput.Present(c, "layer") && actual.Value<string>("layer") != c.Value<string>("layer")) throw new InvalidOperationException("Annotation layer readback differs from request.");
                if (DrawingInput.Present(c, "leader_arrowhead") && actual.Value<string>("leader_arrowhead") != Arrowhead(c.Value<string>("leader_arrowhead")).ToString()) throw new InvalidOperationException("Leader arrowhead readback differs from request.");
                if (DrawingInput.Present(c, "text_override"))
                {
                    var text = item.Entity switch { GeneralDimension x => x.Text.Text, GeneralNote x => x.Text, LeaderNote x => x.Text, SketchedSymbol x => x.GetResultText(PromptBoxes(x.Definition.Sketch)[0]), _ => null };
                    if (text != c.Value<string>("text_override")) throw new InvalidOperationException("Annotation literal text readback differs from request.");
                }
                if (DrawingInput.Present(c, "text_position_mm")) { actual["requested_position_mm"] = c["text_position_mm"]!.DeepClone(); actual["position_adjusted"] = Enumerable.Range(0, 2).Any(i => Math.Abs(actual["position_mm"]![i]!.Value<double>() - c["text_position_mm"]![i]!.Value<double>()) > 0.001); }
                if (item.Entity is not GeneralDimension && actual.Value<bool?>("position_adjusted") == true) throw new InvalidOperationException("Annotation position readback differs from request.");
            }
            return new JObject { ["updated"] = true, ["updated_count"] = rows.Count, ["count"] = rows.Count, ["items"] = rows };
        });
    }
}
#endif
