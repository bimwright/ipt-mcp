#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;

namespace Bimwright.Ipt.Scripting;

/// <summary>Drawing lookups. Sheet coordinates in and out are mm.</summary>
public static partial class Ipt
{
    /// <summary>A visible straight drawing curve of a view, end points in sheet mm.</summary>
    public sealed class ViewLine
    {
        internal ViewLine(DrawingCurve curve, double x0, double y0, double x1, double y1)
        {
            Curve = curve; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
        }

        [Newtonsoft.Json.JsonIgnore] public DrawingCurve Curve { get; }
        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public bool Horizontal => Eq(Y0, Y1, 1e-3);
        public bool Vertical => Eq(X0, X1, 1e-3);
        public double LengthMm => Clean(Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0)));
    }

    /// <summary>
    /// A sheet of a drawing (name/path/Document, null = active) by name ("Sheet:2" or "Sheet") or 1-based index.
    /// No sheet argument = the active sheet.
    /// </summary>
    public static Sheet Sheet(object? drawing = null, object? sheet = null)
    {
        var dwg = Dwg(drawing);
        if (sheet == null) return dwg.ActiveSheet;
        var sheets = new List<Sheet>();
        foreach (Sheet s in dwg.Sheets) sheets.Add(s);
        if (sheet is int index)
            return index >= 1 && index <= sheets.Count ? sheets[index - 1]
                : throw new IptException($"Sheet index {index} is out of range 1..{sheets.Count}.");
        var key = sheet.ToString()!.Trim();
        var hit = sheets.Where(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hit.Count == 0) hit = sheets.Where(s => string.Equals(s.Name.Split(':')[0], key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hit.Count == 1) return hit[0];
        throw new IptException(hit.Count == 0
            ? $"No sheet '{key}'. Sheets: {List(sheets.Select(s => s.Name))}."
            : $"{hit.Count} sheets match '{key}': {List(hit.Select(s => s.Name))}. Use the full name or an index.");
    }

    /// <summary>Every drawing view of a drawing (or of one sheet).</summary>
    public static List<DrawingView> Views(object? drawingOrSheet = null)
    {
        var list = new List<DrawingView>();
        foreach (var s in drawingOrSheet is Sheet one ? new List<Sheet> { one } : AllSheets(drawingOrSheet))
            foreach (DrawingView v in s.DrawingViews) list.Add(v);
        return list;
    }

    /// <summary>A drawing view by name, searched on one sheet or on every sheet of a drawing (null = active drawing).</summary>
    public static DrawingView View(object? drawingOrSheet, string name)
    {
        var views = Views(drawingOrSheet);
        var hit = views.Where(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hit.Count == 1) return hit[0];
        throw new IptException(hit.Count == 0
            ? $"No view '{name}'. Views: {List(views.Select(v => v.Name))}."
            : $"{hit.Count} views are named '{name}' (on different sheets); pass the sheet: Ipt.View(Ipt.Sheet(dwg, \"Sheet:2\"), \"{name}\").");
    }

    /// <summary>Visible straight curves of a view with sheet coordinates in mm (for dimensions and checks).</summary>
    public static List<ViewLine> ViewLines(DrawingView view)
    {
        var list = new List<ViewLine>();
        foreach (DrawingCurve c in view.get_DrawingCurves(null))
        {
            IptGuard.Check("Ipt.ViewLines", 0);
            if (c.CurveType != CurveTypeEnum.kLineSegmentCurve) continue;
            var visible = false;
            foreach (DrawingCurveSegment s in c.Segments) { if (s.Visible) { visible = true; break; } }
            if (!visible) continue;
            var a = c.StartPoint; var b = c.EndPoint;
            list.Add(new ViewLine(c, Clean(Mm(a.X)), Clean(Mm(a.Y)), Clean(Mm(b.X)), Clean(Mm(b.Y))));
        }
        return list;
    }

    /// <summary>
    /// A text on a sheet: a sketch text box (sheet sketch or a sketch inside a view, as sketched balloons and tags are),
    /// a general note or a leader note. Position is in sheet mm; <see cref="Item"/> is the live TextBox/GeneralNote/LeaderNote.
    /// </summary>
    public sealed class SheetText
    {
        internal SheetText(object item, string kind, string text, string sheet, string? view, string? sketch, double x, double y)
        {
            Item = item; Kind = kind; Text = text; Sheet = sheet; View = view; Sketch = sketch; X = x; Y = y;
        }

        [Newtonsoft.Json.JsonIgnore] public object Item { get; }
        /// <summary>sheet_sketch | view_sketch | general_note | leader_note</summary>
        public string Kind { get; }
        public string Text { get; }
        public string Sheet { get; }
        public string? View { get; }
        public string? Sketch { get; }
        public double X { get; }
        public double Y { get; }
        public override string ToString() => $"{Kind} '{Text}' on {Sheet}{(View == null ? "" : " view " + View)} at ({X}, {Y})";
    }

    /// <summary>
    /// Every text of a drawing (or of one sheet): text boxes in sheet sketches and in view sketches, general notes and
    /// leader notes, with sheet, view, sketch and sheet position in mm. Filter with LINQ, then edit or delete <c>Item</c>.
    /// </summary>
    public static List<SheetText> Texts(object? drawingOrSheet = null)
    {
        var list = new List<SheetText>();
        foreach (var s in drawingOrSheet is Sheet one ? new List<Sheet> { one } : AllSheets(drawingOrSheet))
        {
            foreach (DrawingSketch sk in s.Sketches) AddSketchTexts(list, sk, s.Name, null);
            foreach (DrawingView v in s.DrawingViews)
                foreach (DrawingSketch sk in v.Sketches) AddSketchTexts(list, sk, s.Name, v.Name);
            foreach (GeneralNote n in s.DrawingNotes.GeneralNotes)
                list.Add(new SheetText(n, "general_note", Safe(() => n.Text) ?? "", s.Name, null, null, Clean(Mm(n.Position.X)), Clean(Mm(n.Position.Y))));
            foreach (LeaderNote n in s.DrawingNotes.LeaderNotes)
                list.Add(new SheetText(n, "leader_note", Safe(() => n.Text) ?? "", s.Name, null, null, Clean(Mm(n.Position.X)), Clean(Mm(n.Position.Y))));
        }
        return list;
    }

    /// <summary>
    /// A custom table by title (exact, then unique substring, case-insensitive) or 1-based index, on one sheet or on
    /// any sheet of a drawing (null = active drawing). Tables need no name. Throws listing the tables when none or several match.
    /// </summary>
    public static CustomTable Table(object? drawingOrSheet, object titleOrIndex)
    {
        var tables = new List<(CustomTable Table, string Sheet)>();
        foreach (var s in drawingOrSheet is Sheet one ? new List<Sheet> { one } : AllSheets(drawingOrSheet))
            foreach (CustomTable t in s.CustomTables) tables.Add((t, s.Name));
        string Label((CustomTable Table, string Sheet) t) => $"'{Safe(() => t.Table.Title)}' ({t.Sheet})";
        if (titleOrIndex is int index)
            return index >= 1 && index <= tables.Count ? tables[index - 1].Table
                : throw new IptException($"Table index {index} is out of range 1..{tables.Count}. Tables: {List(tables.Select(Label))}.");
        var key = titleOrIndex?.ToString()?.Trim() ?? "";
        var hit = tables.Where(t => string.Equals(Safe(() => t.Table.Title)?.Trim(), key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hit.Count == 0) hit = tables.Where(t => (Safe(() => t.Table.Title) ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (hit.Count == 1) return hit[0].Table;
        throw new IptException(hit.Count == 0
            ? $"No table '{key}'. Tables: {List(tables.Select(Label))}."
            : $"{hit.Count} tables match '{key}': {List(hit.Select(Label))}. Pass the sheet or a fuller title.");
    }

    /// <summary>The cell texts of a custom table, row by row; the first row holds the column titles.</summary>
    public static List<string[]> TableText(CustomTable table)
    {
        var columns = table.Columns.Count;
        var rows = new List<string[]> { Enumerable.Range(1, columns).Select(c => Safe(() => table.Columns[c].Title) ?? "").ToArray() };
        for (var r = 1; r <= table.Rows.Count; r++)
        {
            IptGuard.Check("Ipt.TableText", 0);
            var row = table.Rows[r];
            rows.Add(Enumerable.Range(1, columns).Select(c => Safe(() => row[c].Value) ?? "").ToArray());
        }
        return rows;
    }

    private static void AddSketchTexts(List<SheetText> list, DrawingSketch sketch, string sheet, string? view)
    {
        foreach (TextBox box in sketch.TextBoxes)
        {
            IptGuard.Check("Ipt.Texts", 0);
            var at = sketch.SketchToSheetSpace(box.Origin);
            list.Add(new SheetText(box, view == null ? "sheet_sketch" : "view_sketch", Safe(() => box.Text) ?? "", sheet, view,
                Safe(() => sketch.Name), Clean(Mm(at.X)), Clean(Mm(at.Y))));
        }
    }

    private static List<Sheet> AllSheets(object? drawing)
    {
        var list = new List<Sheet>();
        foreach (Sheet s in Dwg(drawing).Sheets) list.Add(s);
        return list;
    }
}
#endif
