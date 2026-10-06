using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

public static class DrawingTableEditPlan
{
    public static JArray Apply(JArray original, int columns, JObject changes)
    {
        var rows = (JArray)original.DeepClone();
        foreach (var index in (changes["delete_rows"] as JArray ?? new JArray()).Select(x => x.Value<int>()).OrderByDescending(x => x))
        {
            if (index < 1 || index > rows.Count) throw new ArgumentException("Deleted row is outside the original data rows."); rows.RemoveAt(index - 1);
        }
        foreach (JObject insert in changes["insert_rows"] as JArray ?? new JArray())
        {
            var index = insert.Value<int>("index"); var additions = (JArray)insert["rows"]!;
            if (index < 1 || index > rows.Count + 1) throw new ArgumentException("Insert index is outside the current data rows.");
            foreach (JArray row in additions) { if (row.Count != columns) throw new ArgumentException("Inserted row cardinality differs from column count."); rows.Insert(index++ - 1, row.DeepClone()); }
        }
        if (rows.Count > 500) throw new ArgumentException("Final table exceeds 500 data rows.");
        var addresses = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (JObject cell in changes["cells"] as JArray ?? new JArray())
        {
            var row = cell.Value<int>("row"); var column = cell.Value<int>("column");
            if (row < 1 || column < 1 || row > rows.Count || column > columns) throw new ArgumentException("Cell address is outside final data rows/columns.");
            if (!addresses.Add(row + ":" + column)) throw new ArgumentException("Duplicate cell edit.");
            rows[row - 1]![column - 1] = cell["text"]!.DeepClone();
        }
        if (changes["column_widths_mm"] is JArray widths && widths.Count != columns) throw new ArgumentException("column_widths_mm must match column count.");
        if (changes["row_heights_mm"] is JArray heights && heights.Count != rows.Count) throw new ArgumentException("row_heights_mm must match final data row count.");
        return rows;
    }
    public static void RequireDependencies(string[] selected, string[] required)
    {
        if (required.Except(selected, StringComparer.Ordinal).Any()) throw new ArgumentException("Explicit targets must include every dependent item returned by preview.");
    }
    public static void RequireSheetDeletion(int sheets, string[] selected, string[] contents)
    {
        if (sheets <= 1) throw new ArgumentException("Cannot delete the last sheet.");
        if (selected.Length != selected.Distinct(StringComparer.Ordinal).Count() || selected.Length != contents.Length || selected.Except(contents, StringComparer.Ordinal).Any()) throw new ArgumentException("contents must match the exact current sheet inventory.");
    }
}
