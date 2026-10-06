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
    private static CustomTable RecreateTable(Sheet s, CustomTable old)
    {
        var stage = "headings"; try {
        var headings = old.Columns.Cast<Column>().Select(c => c.Title).ToArray();
        var cells = old.Rows.Cast<Row>().SelectMany(r => Enumerable.Range(1, old.Columns.Count).Select(c => r[c].Value)).ToArray();
        var title = old.Title; var position = old.Position;
        var t = s.CustomTables.Add(title, position, headings.Length, old.Rows.Count, ref headings, cells);
        t.Style = old.Style; t.OverrideFormat = old.OverrideFormat; t.Layer = old.Layer;
        t.TitleTextStyle = old.TitleTextStyle; t.ColumnHeaderTextStyle = old.ColumnHeaderTextStyle; t.DataTextStyle = old.DataTextStyle;
        t.ShowTitle = old.ShowTitle; t.HeadingPlacement = old.HeadingPlacement; t.TableDirection = old.TableDirection; t.Rotation = old.Rotation;
        stage = "row spacing"; t.RowLineSpacing = old.RowLineSpacing; stage = "wrap automatic"; t.WrapAutomatically = old.WrapAutomatically; stage = "wrap left"; t.WrapLeft = old.WrapLeft; 
        if (old.Rows.Count == 0) while (t.Rows.Count > 0) t.Rows[t.Rows.Count].Delete();
        for (var c = 1; c <= old.Columns.Count; c++) t.Columns[c].Width = old.Columns[c].Width;
        stage = "row/static flags"; for (var r = 1; r <= old.Rows.Count; r++) { t.Rows[r].Height = old.Rows[r].Height; t.Rows[r].Visible = old.Rows[r].Visible; for (var c = 1; c <= old.Columns.Count; c++) t.Rows[r][c].Static = old.Rows[r][c].Static; }
        stage = "attributes"; CopyDrawingAttributes(old.AttributeSets, t.AttributeSets);
        stage = "title/position/delete"; t.Title = title; t.Position = position; old.Delete(); return t; } catch (Exception ex) { throw new InvalidOperationException("Table snapshot at " + stage + ": " + ex.Message, ex); }
    }
    private static double HeaderHeight(InventorCommandContext ctx, DrawingDocument d, Sheet s, CustomTable table, double? requested)
    {
        if (table.HeadingPlacement == HeadingPlacementEnum.kNoHeading || table.NumberOfSections != 1 || Math.Abs(table.Rotation) > 1e-9) throw new NotSupportedException("Header-height rebuild requires one unrotated section with visible headings.");
        var style = (TableStyle)table.Style.Copy("BimwrightTable_" + Guid.NewGuid().ToString("N"));
        var headings = table.Columns.Cast<Column>().Select(c => c.Title).ToArray();
        var probe = s.CustomTables.Add("Header measurement", table.Position, headings.Length, 0, ref headings);
        try
        {
            probe.Style = style; probe.ColumnHeaderTextStyle = table.ColumnHeaderTextStyle; probe.OverrideFormat = table.OverrideFormat;
            probe.ShowTitle = false; probe.HeadingPlacement = HeadingPlacementEnum.kHeadingAtTop;
            while (probe.Rows.Count > 0) probe.Rows[probe.Rows.Count].Delete();
            for (var c = 1; c <= table.Columns.Count; c++) probe.Columns[c].Width = table.Columns[c].Width;
            double Measure(double gap) { style.HeadingGap = gap; d.Update(); return (probe.RangeBox.MaxPoint.Y - probe.RangeBox.MinPoint.Y) * 10; }
            if (!requested.HasValue) return Measure(table.Style.HeadingGap);
            var first = Measure(0.2); var second = Measure(0.4); var slope = (second - first) / 0.2;
            if (slope <= 0) throw new NotSupportedException("Native header-height calibration failed.");
            var gap = 0.2 + (requested.Value - first) / slope;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (gap <= 0 || double.IsNaN(gap) || double.IsInfinity(gap)) throw new ArgumentException("Requested header height is below the native minimum.");
                var actual = Measure(gap);
                if (Math.Abs(actual - requested.Value) < 0.01)
                {
                    var format = table.OverrideFormat; var headerText = table.ColumnHeaderTextStyle; var titleText = table.TitleTextStyle; var dataText = table.DataTextStyle;
                    var layer = table.Layer; var showTitle = table.ShowTitle; var heading = table.HeadingPlacement; var direction = table.TableDirection; var spacing = table.RowLineSpacing;
                    var widths = table.Columns.Cast<Column>().Select(x => x.Width).ToArray(); var heights = table.Rows.Cast<Row>().Select(x => x.Height).ToArray();
                    table.Style = style; table.OverrideFormat = format; table.ColumnHeaderTextStyle = headerText; table.TitleTextStyle = titleText; table.DataTextStyle = dataText;
                    table.Layer = layer; table.ShowTitle = showTitle; table.HeadingPlacement = heading; table.TableDirection = direction; table.RowLineSpacing = spacing;
                    for (var c = 0; c < widths.Length; c++) table.Columns[c + 1].Width = widths[c];
                    for (var r = 0; r < heights.Length; r++) table.Rows[r + 1].Height = heights[r];
                    return actual;
                }
                gap += (requested.Value - actual) / slope;
            }
            throw new ArgumentException("Native header-height readback differs from request.");
        }
        finally { probe.Delete(); }
    }
    private static InventorCommandResult EditTable(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var item = ResolveItem(d, s, Inventory(s), new JObject { ["kind"] = "table", ["name"] = p["name"]!.DeepClone() }); var original = (CustomTable)item.Entity;
        if (original.TableSource != TableSourceTypeEnum.kNoTableSource) throw new NotSupportedException("Only unlinked custom tables can be edited; linked/BOM tables are unavailable.");
        var c = (JObject)p["changes"]!; var before = TableInfo(original);
        var expectedRows = DrawingTableEditPlan.Apply((JArray)before["rows"]!, original.Columns.Count, c);
        var style = DrawingInput.Present(c, "style") ? Definition(d.StylesManager.TableStyles.Cast<TableStyle>(), c.Value<string>("style"), null, x => x.Name) : null;
        return DrawingSupport.Atomic(ctx, d, "Edit drawing table", () =>
        {
            var t = p.Value<bool?>("rebuild") == true ? RecreateTable(s, original) : original;
            if (style != null) t.Style = style;
            foreach (var index in (c["delete_rows"] as JArray ?? new JArray()).Select(x => x.Value<int>()).OrderByDescending(x => x)) t.Rows[index].Delete();
            foreach (JObject insert in c["insert_rows"] as JArray ?? new JArray())
            {
                var index = insert.Value<int>("index");
                foreach (JArray row in (JArray)insert["rows"]!) { t.Rows.Add(index > t.Rows.Count ? 0 : index, index <= t.Rows.Count, row.Select(x => x.Value<string>()!).ToArray()); index++; }
            }
            foreach (JObject cell in c["cells"] as JArray ?? new JArray()) t.Rows[cell.Value<int>("row")][cell.Value<int>("column")].Value = cell.Value<string>("text");
            var widths = c["column_widths_mm"] as JArray ?? (JArray)new JArray(((JArray)before["columns"]!).Select(x => x.Value<double>("width_mm")));
            for (var i = 0; i < widths.Count; i++) t.Columns[i + 1].Width = widths[i].Value<double>() / 10;
            if (c["row_heights_mm"] is JArray heights) for (var i = 0; i < heights.Count; i++) t.Rows[i + 1].Height = heights[i].Value<double>() / 10;
            var title = c.Value<string>("title") ?? before.Value<string>("title")!;
            double? header = DrawingInput.Present(c, "header_height_mm") ? HeaderHeight(ctx, d, s, t, c.Value<double>("header_height_mm")) : null;
            t.Title = title; t.TableDirection = before.Value<string>("anchor") == "top_left" ? TableDirectionEnum.kTopDownDirection : TableDirectionEnum.kBottomUpDirection;
            t.Position = DrawingSupport.Point(DrawingSupport.App(ctx), c["position_mm"] ?? before["position_mm"]);
            var actual = TableInfo(t);
            if (!JToken.DeepEquals(actual["rows"], expectedRows) || actual.Value<string>("title") != title || Enumerable.Range(0, 2).Any(i => Math.Abs(actual["position_mm"]![i]!.Value<double>() - (c["position_mm"] ?? before["position_mm"])![i]!.Value<double>()) > 0.001) || widths.Select((w, i) => Math.Abs(w.Value<double>() - actual["columns"]![i]!.Value<double>("width_mm")) > 0.001).Any(x => x)) throw new InvalidOperationException("Edited table content/placement/width readback differs from request.");
            if (c["row_heights_mm"] is JArray wanted && wanted.Select((h, i) => Math.Abs(h.Value<double>() - actual["row_heights_mm"]![i]!.Value<double>()) > 0.001).Any(x => x)) throw new InvalidOperationException("Edited row-height readback differs from request.");
            if (header.HasValue) actual["header_height_mm"] = header.Value;
            actual["locator"] = new DrawingItem(t, "table").Target(d, s)["locator"]!.DeepClone(); actual["updated"] = true; actual["count"] = 1; actual["rebuilt"] = p.Value<bool?>("rebuild") == true;
            return actual;
        });
    }
}
#endif
