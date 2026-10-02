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
    private static bool OtherNamedItem(Sheet s, string name) =>
        s.DrawingDimensions.GeneralDimensions.Cast<GeneralDimension>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) ||
        s.SketchedSymbols.Cast<SketchedSymbol>().Any(x => (DrawingSupport.Read(x.AttributeSets, "name") ?? x.Name) == name) ||
        s.Balloons.Cast<Balloon>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) ||
        s.Centermarks.Cast<Centermark>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name);

    private static bool NamedNoteOrTable(Sheet s, string name) =>
        s.DrawingNotes.GeneralNotes.Cast<GeneralNote>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) ||
        s.DrawingNotes.LeaderNotes.Cast<LeaderNote>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) ||
        s.CustomTables.Cast<CustomTable>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name);

    private static JObject BoxInfo(Box2d box) => new() { ["min"] = new JArray(box.MinPoint.X * 10, box.MinPoint.Y * 10), ["max"] = new JArray(box.MaxPoint.X * 10, box.MaxPoint.Y * 10) };
    private static JObject NoteInfo(GeneralNote n) => new() { ["name"] = DrawingSupport.Read(n.AttributeSets, "name"), ["kind"] = DrawingSupport.Read(n.AttributeSets, "note_kind") ?? "general", ["text"] = n.Text, ["position_mm"] = new JArray(n.Position.X * 10, n.Position.Y * 10), ["style"] = n.TextStyle.Name, ["layer"] = n.Layer.Name, ["attached"] = false, ["box_mm"] = BoxInfo(n.RangeBox), ["width_mm"] = n.Width * 10, ["height_mm"] = n.Height * 10 };
    private static JObject NoteInfo(LeaderNote n) => new() { ["name"] = DrawingSupport.Read(n.AttributeSets, "name"), ["kind"] = "leader", ["text"] = n.Text, ["position_mm"] = new JArray(n.Position.X * 10, n.Position.Y * 10), ["style"] = n.DimensionStyle.Name, ["layer"] = n.Layer.Name, ["attached"] = n.Leader.AllLeafNodes.Cast<LeaderNode>().Any(x => x.AttachedEntity?.Geometry is DrawingCurve), ["box_mm"] = BoxInfo(n.RangeBox) };
    private static bool SamePlacement(JObject actual, JObject requested) =>
        actual.Value<string>("text") == requested.Value<string>("text") &&
        Enumerable.Range(0, 2).All(i => Math.Abs((double)actual["position_mm"]![i]! - (double)requested["position_mm"]![i]!) < 0.001) &&
        new[] { "style", "layer" }.All(key => !DrawingInput.Present(requested, key) || actual.Value<string>(key) == requested.Value<string>(key)) &&
        (requested["box_mm"] is not JObject box || new[] { "width", "height" }.All(key => Math.Abs(actual.Value<double>(key + "_mm") - box.Value<double>(key)) < 0.001));

    private static InventorCommandResult AddNote(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        DrawingSupport.RequireAnnotations(s);
        var name = p.Value<string>("name")!; var kind = p.Value<string>("kind")!;
        var resolved = kind == "leader" ? DrawingGeometry.Resolve(s, DrawingSupport.View(s, p.Value<string>("view")), (JObject)p["intent"]!, 0.5) : null;
        var general = s.DrawingNotes.GeneralNotes.Cast<GeneralNote>().Where(x => DrawingSupport.Read(x.AttributeSets, "name") == name).ToArray();
        var leaders = s.DrawingNotes.LeaderNotes.Cast<LeaderNote>().Where(x => DrawingSupport.Read(x.AttributeSets, "name") == name).ToArray();
        if (OtherNamedItem(s, name) || s.CustomTables.Cast<CustomTable>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) || general.Length + leaders.Length > 1) throw new ArgumentException("Drawing item name conflicts: " + name);
        if (general.Length + leaders.Length == 1)
        {
            var actual = general.Length == 1 ? NoteInfo(general[0]) : NoteInfo(leaders[0]);
            DrawingSupport.Existing(general.Length == 1 ? general[0].AttributeSets : leaders[0].AttributeSets, p);
            if (!SamePlacement(actual, p) || kind == "leader" && !actual.Value<bool>("attached")) throw new ArgumentException("Managed note no longer matches requested text/placement/attachment; query and edit explicitly.");
            if (kind == "leader" && !leaders[0].Leader.AllLeafNodes.Cast<LeaderNode>().Any(n => n.AttachedEntity?.Geometry is DrawingCurve curve && curve.Equals(resolved!.Curve) && n.AttachedEntity.PointOnSheet.DistanceTo(resolved.Point) < 0.0001)) throw new ArgumentException("Managed leader note attachment differs from the requested intent.");
            actual["created"] = false; actual["created_count"] = 0; return DrawingSupport.Success(ctx, actual);
        }
        var layer = DrawingInput.Present(p, "layer") ? Definition(d.StylesManager.Layers.Cast<Layer>(), p.Value<string>("layer"), null, x => x.Name) : null;
        var textStyle = kind != "leader" && DrawingInput.Present(p, "style") ? Definition(d.StylesManager.TextStyles.Cast<TextStyle>(), p.Value<string>("style"), null, x => x.Name) : null;
        var dimStyle = kind == "leader" && DrawingInput.Present(p, "style") ? Definition(d.StylesManager.DimensionStyles.Cast<DimensionStyle>(), p.Value<string>("style"), null, x => x.Name) : null;
        var app = DrawingSupport.App(ctx); var position = DrawingSupport.Point(app, p["position_mm"]);
        return DrawingSupport.Atomic(ctx, d, "Add drawing note", () =>
        {
            JObject actual;
            var formatted = SecurityElement.Escape(p.Value<string>("text")!);
            if (kind == "leader")
            {
                var points = app.TransientObjects.CreateObjectCollection(); points.Add(position); points.Add(resolved!.Intent);
                var n = s.DrawingNotes.LeaderNotes.Add(points, formatted, (object?)dimStyle ?? Type.Missing);
                n.Position = position; if (layer != null) n.Layer = layer;
                DrawingSupport.Mark(n.AttributeSets, name, p); actual = NoteInfo(n);
                if (!actual.Value<bool>("attached")) throw new InvalidOperationException("Created leader note is not attached.");
            }
            else
            {
                var n = p["box_mm"] is JObject box
                    ? s.DrawingNotes.GeneralNotes.AddByRectangle(position, app.TransientGeometry.CreatePoint2d(position.X + box.Value<double>("width") / 10, position.Y - box.Value<double>("height") / 10), formatted, (object?)textStyle ?? Type.Missing)
                    : s.DrawingNotes.GeneralNotes.AddFitted(position, formatted, (object?)textStyle ?? Type.Missing);
                if (p["box_mm"] is JObject size)
                {
                    n.HorizontalJustification = HorizontalTextAlignmentEnum.kAlignTextLeft;
                    n.VerticalJustification = VerticalTextAlignmentEnum.kAlignTextUpper;
                    n.Width = size.Value<double>("width") / 10; n.Height = size.Value<double>("height") / 10;
                }
                n.Position = position; if (layer != null) n.Layer = layer;
                DrawingSupport.Mark(n.AttributeSets, name, p); DrawingSupport.Write(n.AttributeSets, "note_kind", kind); actual = NoteInfo(n);
            }
            if (!SamePlacement(actual, p)) throw new InvalidOperationException("Note text/placement/style readback differs from request.");
            actual["created"] = true; actual["created_count"] = 1; return actual;
        });
    }

    private static JObject TableInfo(CustomTable t) => new()
    {
        ["name"] = DrawingSupport.Read(t.AttributeSets, "name"), ["title"] = t.Title, ["style"] = t.Style.Name, ["anchor"] = t.TableDirection == TableDirectionEnum.kTopDownDirection ? "top_left" : "bottom_left",
        ["position_mm"] = new JArray(t.Position.X * 10, t.Position.Y * 10), ["box_mm"] = BoxInfo(t.RangeBox),
        ["columns"] = new JArray(t.Columns.Cast<Column>().Select(c => new JObject { ["heading"] = c.Title, ["width_mm"] = c.Width * 10 })),
        ["rows"] = new JArray(t.Rows.Cast<Row>().Select(r => new JArray(Enumerable.Range(1, t.Columns.Count).Select(i => r[i].Value)))),
        ["row_heights_mm"] = new JArray(t.Rows.Cast<Row>().Select(r => r.Height * 10))
    };
    private static bool SameTable(JObject actual, JObject p) =>
        actual.Value<string>("title") == (p.Value<string>("title") ?? p.Value<string>("name")) &&
        actual.Value<string>("anchor") == "top_left" &&
        JToken.DeepEquals(actual["rows"], p["rows"]) &&
        Enumerable.Range(0, 2).All(i => Math.Abs((double)actual["position_mm"]![i]! - (double)p["position_mm"]![i]!) < 0.001) &&
        ((JArray)p["columns"]!).Count == ((JArray)actual["columns"]!).Count &&
        ((JArray)p["columns"]!).Select((c, i) => c.Value<string>("heading") == actual["columns"]![i]!.Value<string>("heading") && Math.Abs(c.Value<double>("width_mm") - actual["columns"]![i]!.Value<double>("width_mm")) < 0.001).All(x => x) &&
        (!DrawingInput.Present(p, "row_heights_mm") || ((JArray)p["row_heights_mm"]!).Select((h, i) => Math.Abs(h.Value<double>() - actual["row_heights_mm"]![i]!.Value<double>()) < 0.001).All(x => x)) &&
        (!DrawingInput.Present(p, "style") || actual.Value<string>("style") == p.Value<string>("style"));
    private static JObject TableQueryInfo(CustomTable t, int max, int offset)
    {
        var result = TableInfo(t);
        foreach (var key in new[] { "columns", "rows", "row_heights_mm" }) result[key] = Page((JArray)result[key]!, max, offset);
        return result;
    }
    private static InventorCommandResult AddTable(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        DrawingSupport.RequireAnnotations(s);
        var name = p.Value<string>("name")!;
        var found = s.CustomTables.Cast<CustomTable>().Where(t => DrawingSupport.Read(t.AttributeSets, "name") == name).ToArray();
        if (OtherNamedItem(s, name) || s.DrawingNotes.GeneralNotes.Cast<GeneralNote>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) || s.DrawingNotes.LeaderNotes.Cast<LeaderNote>().Any(x => DrawingSupport.Read(x.AttributeSets, "name") == name) || found.Length > 1) throw new ArgumentException("Drawing item name conflicts: " + name);
        if (found.Length == 1)
        {
            DrawingSupport.Existing(found[0].AttributeSets, p); var actual = TableInfo(found[0]);
            if (!SameTable(actual, p)) throw new ArgumentException("Managed table no longer matches requested content/dimensions; query and edit explicitly.");
            actual["created"] = false; actual["created_count"] = 0; return DrawingSupport.Success(ctx, actual);
        }
        var style = DrawingInput.Present(p, "style") ? Definition(d.StylesManager.TableStyles.Cast<TableStyle>(), p.Value<string>("style"), null, x => x.Name) : null;
        var columns = ((JArray)p["columns"]!).Cast<JObject>().ToArray(); var rows = ((JArray)p["rows"]!).Cast<JArray>().ToArray();
        var widths = columns.Select(c => c.Value<double>("width_mm") / 10).ToArray();
        var heights = p["row_heights_mm"] is JArray h ? h.Select(x => x.Value<double>() / 10).ToArray() : null;
        return DrawingSupport.Atomic(ctx, d, "Add drawing table", () =>
        {
            var headings = columns.Select(c => c.Value<string>("heading")!).ToArray();
            var t = s.CustomTables.Add(name, DrawingSupport.Point(DrawingSupport.App(ctx), p["position_mm"]), columns.Length, rows.Length, ref headings, rows.SelectMany(r => r.Select(x => x.Value<string>()!)).ToArray());
            if (style != null) t.Style = style;
            t.TableDirection = TableDirectionEnum.kTopDownDirection;
            // Native Add interprets zero as its ten-row default; remove those rows explicitly.
            if (rows.Length == 0) while (t.Rows.Count > 0) t.Rows[t.Rows.Count].Delete();
            for (var i = 0; i < widths.Length; i++) t.Columns[i + 1].Width = widths[i];
            if (heights != null) for (var i = 0; i < heights.Length; i++) t.Rows[i + 1].Height = heights[i];
            t.Title = p.Value<string>("title") ?? name;
            // Applying a bottom-up style moves the native insertion point. Restore it last.
            t.Position = DrawingSupport.Point(DrawingSupport.App(ctx), p["position_mm"]);
            DrawingSupport.Mark(t.AttributeSets, name, p); var actual = TableInfo(t);
            if (!SameTable(actual, p)) throw new InvalidOperationException("Table content/dimension/style readback differs from request (style=" + actual.Value<string>("style") + ", anchor=" + actual.Value<string>("anchor") + ").");
            actual["created"] = true; actual["created_count"] = 1; return actual;
        });
    }
}
#endif
