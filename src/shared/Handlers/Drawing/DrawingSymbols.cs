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
    private static JObject SymbolPrompts(SketchedSymbol symbol) => new(PromptBoxes(symbol.Definition.Sketch).Select(box => new JProperty(PromptLabel(box), symbol.GetResultText(box))));
    private static JObject SymbolInfo(SketchedSymbol x) => new()
    {
        ["name"] = DrawingSupport.Read(x.AttributeSets, "name") ?? x.Name, ["kind"] = SymbolKind(x), ["definition"] = x.Definition.Name,
        ["position_mm"] = new JArray(x.Position.X * 10, x.Position.Y * 10), ["rotation_deg"] = x.Rotation * 180 / Math.PI, ["scale"] = x.Scale,
        ["prompts"] = SymbolPrompts(x), ["style"] = x.LeaderStyle.Name, ["layer"] = x.Layer.Name,
        ["attached"] = LeaderIntents(x.Leader).Any(i => i.Geometry is DrawingCurve), ["leader_arrowhead"] = x.LeaderStyle.ArrowheadType.ToString()
    };
    private static bool AttachedTo(DrawingItem item, DrawingIntent expected) => ItemIntents(item).Any(i => i.Geometry.Equals(expected.Curve) && i.PointOnSheet.DistanceTo(expected.Point) < 0.0001);
    private static InventorCommandResult AddSymbol(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var inventory = Inventory(s); var name = p.Value<string>("name")!; var kind = p.Value<string>("kind")!;
        var found = inventory.Where(i => i.Name == name).ToArray();
        if (found.Length > 1 || found.Length == 1 && found[0].Kind != kind) throw new ArgumentException("Drawing item name conflicts: " + name);
        var intent = DrawingInput.Present(p, "intent") ? DrawingGeometry.Resolve(s, DrawingSupport.View(s, p.Value<string>("view")), (JObject)p["intent"]!, 0.5) : null;
        if (kind == "centermark" && intent!.Curve.ProjectedCurveType != Curve2dTypeEnum.kCircleCurve2d && intent.Curve.ProjectedCurveType != Curve2dTypeEnum.kCircularArcCurve2d) throw new ArgumentException("Centermark requires a circular projected intent.");
        var layer = DrawingInput.Present(p, "layer") ? Definition(d.StylesManager.Layers.Cast<Layer>(), p.Value<string>("layer"), null, x => x.Name) : null;
        var definition = kind == "symbol" ? Definition(d.SketchedSymbolDefinitions.Cast<SketchedSymbolDefinition>(), p.Value<string>("definition"), null, x => x.Name) : null;
        var strings = definition == null ? null : Prompts(definition.Sketch, p["prompts"] as JObject);
        var leaderStyle = kind == "symbol" && DrawingInput.Present(p, "style") ? Definition(d.StylesManager.LeaderStyles.Cast<LeaderStyle>(), p.Value<string>("style"), null, x => x.Name) : null;
        var centerStyle = kind == "centermark" && DrawingInput.Present(p, "style") ? Definition(d.StylesManager.CentermarkStyles.Cast<CentermarkStyle>(), p.Value<string>("style"), null, x => x.Name) : null;
        bool Matches(DrawingItem item)
        {
            var info = ItemInfo(d, s, item);
            if (new[] { "style", "layer" }.Any(k => DrawingInput.Present(p, k) && info.Value<string>(k) != p.Value<string>(k))) return false;
            if (intent != null && !AttachedTo(item, intent)) return false;
            if (kind == "centermark") return info.Value<bool>("attached");
            var symbol = (SketchedSymbol)item.Entity;
            return symbol.Definition.Name == definition!.Name && Math.Abs(symbol.Scale - p.Value<double>("scale")) < 1e-6 && Math.Abs(symbol.Rotation * 180 / Math.PI - p.Value<double>("rotation_deg")) < 0.0001 && symbol.Position.DistanceTo(DrawingSupport.Point(DrawingSupport.App(ctx), p["position_mm"])) < 0.0001 && PromptBoxes(definition.Sketch).Select((b, i) => symbol.GetResultText(b) == strings![i]).All(x => x) && (intent != null || !LeaderIntents(symbol.Leader).Any());
        }
        if (found.Length == 1)
        {
            DrawingSupport.Existing(found[0].Attributes!, p);
            if (!Matches(found[0])) throw new ArgumentException("Managed symbol no longer matches requested placement/prompts/attachment.");
            var info = ItemInfo(d, s, found[0]); info["created"] = false; info["created_count"] = 0; return DrawingSupport.Success(ctx, info);
        }
        return DrawingSupport.Atomic(ctx, d, "Add drawing symbol", () =>
        {
            DrawingItem item;
            if (kind == "centermark") item = new DrawingItem(s.Centermarks.Add(intent!.Intent, true, false, (object?)centerStyle ?? Type.Missing, (object?)layer ?? Type.Missing), kind);
            else
            {
                var app = DrawingSupport.App(ctx); var position = DrawingSupport.Point(app, p["position_mm"]); var rotation = p.Value<double>("rotation_deg") * Math.PI / 180; var scale = p.Value<double>("scale");
                SketchedSymbol symbol;
                if (intent == null) symbol = s.SketchedSymbols.Add(definition!, position, rotation, scale, strings!);
                else { var points = app.TransientObjects.CreateObjectCollection(); points.Add(position); points.Add(intent.Intent); symbol = s.SketchedSymbols.AddWithLeader(definition!, points, rotation, scale, strings!, false, true); }
                if (leaderStyle != null) symbol.LeaderStyle = leaderStyle; if (layer != null) symbol.Layer = layer;
                item = new DrawingItem(symbol, kind);
            }
            DrawingSupport.Mark(item.Attributes!, name, p); DrawingSupport.Write(item.Attributes!, "drawing_kind", kind);
            if (!Matches(item)) throw new InvalidOperationException("Symbol placement/prompts/attachment readback differs from request.");
            var actual = ItemInfo(d, s, item); actual["created"] = true; actual["created_count"] = 1; return actual;
        });
    }
}
#endif
