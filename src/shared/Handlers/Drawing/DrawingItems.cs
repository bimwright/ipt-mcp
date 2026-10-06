#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    private static void CopyDrawingAttributes(AttributeSets source, AttributeSets target)
    {
        foreach (AttributeSet set in source)
        {
            var copy = target.NameIsUsed[set.Name] ? target[set.Name] : target.Add(set.Name);
            foreach (Inventor.Attribute attribute in set)
            {
                if (copy.NameIsUsed[attribute.Name]) copy[attribute.Name].Value = attribute.Value;
                else copy.Add(attribute.Name, attribute.ValueType, attribute.Value);
            }
        }
    }
    private sealed class DrawingItem
    {
        internal object Entity { get; }
        internal string Kind { get; }
        internal DrawingItem(object entity, string kind) { Entity = entity; Kind = kind; }
        internal AttributeSets? Attributes => Entity switch { DrawingView x => x.AttributeSets, GeneralDimension x => x.AttributeSets, GeneralNote x => x.AttributeSets, LeaderNote x => x.AttributeSets, SketchedSymbol x => x.AttributeSets, Centermark x => x.AttributeSets, CustomTable x => x.AttributeSets, Balloon x => x.AttributeSets, _ => null };
        internal string? Name => Entity is DrawingView v ? v.Name : Entity is Border b ? b.Name : Entity is TitleBlock t ? t.Name : Attributes != null ? DrawingSupport.Read(Attributes, "name") ?? (Entity as SketchedSymbol)?.Name : null;
        internal byte[] ReferenceKey()
        {
            byte[] key = Array.Empty<byte>();
            switch (Entity)
            {
                case DrawingView x: x.GetReferenceKey(ref key, 0); break;
                case GeneralDimension x: x.GetReferenceKey(ref key, 0); break;
                case GeneralNote x: x.GetReferenceKey(ref key, 0); break;
                case LeaderNote x: x.GetReferenceKey(ref key, 0); break;
                case SketchedSymbol x: x.GetReferenceKey(ref key, 0); break;
                case Centermark x: x.GetReferenceKey(ref key, 0); break;
                case CustomTable x: x.GetReferenceKey(ref key, 0); break;
                case Balloon x: x.GetReferenceKey(ref key, 0); break;
            }
            return key;
        }
        internal string Key => Kind + ":" + (Entity is Border || Entity is TitleBlock ? Name : Convert.ToBase64String(ReferenceKey()));
        internal JObject Target(DrawingDocument d, Sheet s)
        {
            var result = new JObject { ["kind"] = Kind };
            if (Entity is Border || Entity is TitleBlock) result["name"] = Name;
            if (Entity is not Border && Entity is not TitleBlock) result["locator"] = new JObject { ["document_id"] = d.InternalName, ["sheet_id"] = s.InternalName, ["kind"] = Kind, ["reference_key"] = Convert.ToBase64String(ReferenceKey()) };
            return result;
        }
        internal JObject? Box => Entity switch { DrawingView x => (JObject)DrawingSupport.ViewInfo(x)["box_mm"]!, GeneralNote x => BoxInfo(x.RangeBox), LeaderNote x => BoxInfo(x.RangeBox), CustomTable x => BoxInfo(x.RangeBox), Border x => BoxInfo(x.RangeBox), TitleBlock x => BoxInfo(x.RangeBox), _ => null };
        internal void Delete()
        {
            switch (Entity) { case DrawingView x: x.Delete(); break; case GeneralDimension x: x.Delete(); break; case GeneralNote x: x.Delete(); break; case LeaderNote x: x.Delete(); break; case SketchedSymbol x: x.Delete(); break; case Centermark x: x.Delete(); break; case CustomTable x: x.Delete(); break; case Balloon x: x.Delete(); break; case Border x: x.Delete(); break; case TitleBlock x: x.Delete(); break; default: throw new NotSupportedException("Unsupported drawing item."); }
        }
    }
    private static string SymbolKind(SketchedSymbol symbol)
    {
        var kind = DrawingSupport.Read(symbol.AttributeSets, "drawing_kind"); if (kind != null) return kind;
        var signature = DrawingSupport.Read(symbol.AttributeSets, "signature");
        if (signature != null) { try { var input = JObject.Parse(signature); if (input["item"] != null && input.Value<string>("mode") == "symbol") return "balloon"; } catch (Newtonsoft.Json.JsonReaderException) { } }
        return "symbol";
    }
    private static DrawingItem[] Inventory(Sheet s, bool layout = false)
    {
        DrawingSupport.RequireAnnotations(s);
        var items = s.DrawingViews.Cast<DrawingView>().Select(x => new DrawingItem(x, "view"))
            .Concat(s.DrawingDimensions.GeneralDimensions.Cast<GeneralDimension>().Select(x => new DrawingItem(x, "dimension")))
            .Concat(s.DrawingNotes.GeneralNotes.Cast<GeneralNote>().Select(x => new DrawingItem(x, "note")))
            .Concat(s.DrawingNotes.LeaderNotes.Cast<LeaderNote>().Select(x => new DrawingItem(x, "note")))
            .Concat(s.SketchedSymbols.Cast<SketchedSymbol>().Select(x => new DrawingItem(x, SymbolKind(x))))
            .Concat(s.Centermarks.Cast<Centermark>().Select(x => new DrawingItem(x, "centermark")))
            .Concat(s.CustomTables.Cast<CustomTable>().Select(x => new DrawingItem(x, "table")))
            .Concat(s.Balloons.Cast<Balloon>().Select(x => new DrawingItem(x, "balloon"))).ToList();
        if (layout) { if (s.Border != null) items.Add(new DrawingItem(s.Border, "border")); if (s.TitleBlock != null) items.Add(new DrawingItem(s.TitleBlock, "title_block")); }
        return items.ToArray();
    }
    private static DrawingItem ResolveItem(DrawingDocument d, Sheet s, DrawingItem[] inventory, JObject target)
    {
        DrawingPhase2Input.Target(target);
        var candidates = inventory.Where(x => x.Kind == target.Value<string>("kind")).ToArray();
        if (target["locator"] is JObject locator)
        {
            if (locator.Value<string>("document_id") != d.InternalName || locator.Value<string>("sheet_id") != s.InternalName) throw new ArgumentException("Locator belongs to another document or sheet.");
            var key = Convert.FromBase64String(locator.Value<string>("reference_key")!); object match;
            object bound;
            try { bound = d.ReferenceKeyManager.BindKeyToObject(ref key, 0, out match); }
            catch (Exception ex) { throw new ArgumentException("Stale or invalid drawing locator: " + ex.Message); }
            candidates = candidates.Where(x => x.Entity.Equals(bound) && Convert.ToBase64String(x.ReferenceKey()) == locator.Value<string>("reference_key")).ToArray();
        }
        else candidates = candidates.Where(x => x.Name == target.Value<string>("name")).ToArray();
        if (candidates.Length != 1) throw new ArgumentException("Drawing target must resolve uniquely on the selected sheet.");
        return candidates[0];
    }
    private static IEnumerable<GeometryIntent> LeaderIntents(Leader? leader) => leader == null || !leader.HasRootNode ? Array.Empty<GeometryIntent>() : leader.AllLeafNodes.Cast<LeaderNode>().Select(n => n.AttachedEntity).Where(x => x != null)!;
    private static Point2d? IntentPoint(GeometryIntent intent) => intent.IntentType == IntentTypeEnum.kNoPointIntent || intent.IntentType == IntentTypeEnum.kGeometryIntent ? null : intent.PointOnSheet;
    private static IEnumerable<GeometryIntent> ItemIntents(DrawingItem item) => item.Entity switch
    {
        LinearGeneralDimension x => new[] { x.IntentOne, x.IntentTwo, x.IntentThree }.Where(i => i != null), AngularGeneralDimension x => new[] { x.IntentOne, x.IntentTwo, x.IntentThree }.Where(i => i != null),
        DiameterGeneralDimension x => new[] { x.Intent }, RadiusGeneralDimension x => new[] { x.Intent },
        LeaderNote x => LeaderIntents(x.Leader), SketchedSymbol x => LeaderIntents(x.Leader), Balloon x => LeaderIntents(x.Leader),
        Centermark x => x.AttachedEntity is GeometryIntent intent ? new[] { intent } : throw new NotSupportedException("Unverified centermark attachment type."),
        GeneralDimension => throw new NotSupportedException("Unverified dimension attachment type."), _ => Array.Empty<GeometryIntent>()
    };
    private static JObject ItemInfo(DrawingDocument d, Sheet s, DrawingItem item)
    {
        JObject data = item.Entity switch
        {
            DrawingView x => DrawingSupport.ViewInfo(x), GeneralNote x => NoteInfo(x), LeaderNote x => NoteInfo(x), CustomTable x => TableInfo(x),
            GeneralDimension x => new JObject { ["type"] = x.Type.ToString(), ["text"] = x.Text.FormattedText, ["precision"] = x.Precision, ["value"] = x is AngularGeneralDimension ? x.ModelValue * 180 / Math.PI : x.ModelValue * 10, ["unit"] = x is AngularGeneralDimension ? "deg" : "mm", ["position_mm"] = new JArray(x.Text.Origin.X * 10, x.Text.Origin.Y * 10), ["attached"] = x.Attached, ["style"] = x.Style.Name, ["layer"] = x.Layer.Name },
            SketchedSymbol x => SymbolInfo(x), Centermark x => new JObject { ["attached"] = x.Attached, ["position_mm"] = new JArray(x.Position.X * 10, x.Position.Y * 10), ["style"] = x.Style.Name, ["layer"] = x.Layer.Name },
            Balloon x => new JObject { ["attached"] = x.Attached, ["position_mm"] = new JArray(x.Position.X * 10, x.Position.Y * 10), ["style"] = x.Style.Name, ["layer"] = x.Layer.Name }, _ => new JObject()
        };
        data["selection"] = item.Target(d, s); data["locator"] = data["selection"]!["locator"]?.DeepClone(); data["name"] = item.Name;
        if (item.Entity is LeaderNote leader) data["leader_arrowhead"] = leader.DimensionStyle.LeaderStyle.ArrowheadType.ToString();
        return data;
    }
}
#endif
