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
    private static void RequireSupportedDeletion(Sheet s)
    {
        if (s.DrawingNotes.Count != s.DrawingNotes.GeneralNotes.Count + s.DrawingNotes.LeaderNotes.Count || s.DrawingDimensions.OrdinateDimensions.Count != 0 || s.DrawingDimensions.BaselineDimensionSets.Count != 0 || s.DrawingDimensions.OrdinateDimensionSets.Count != 0 || s.DrawingDimensions.ChainDimensionSets.Count != 0 || s.PartsLists.Count != 0 || s.RevisionTables.Count != 0 || s.HoleTables.Count != 0 || s.FeatureControlFrames.Count != 0 || s.SurfaceTextureSymbols.Count != 0 || s.Centerlines.Count != 0 || s.WeldingSymbols.Count != 0 || s.EdgeSymbols.Count != 0 || s.Sketches.Count != 0 || s.DrawingViews.Cast<DrawingView>().Any(v => v.Sketches.Count != 0)) throw new NotSupportedException("Sheet contains unsupported annotation/table/sketch types; dependent deletion cannot be proven. Remove them explicitly in Inventor and query again.");
    }
    private static bool Descendant(DrawingView child, DrawingView parent)
    {
        var current = child;
        var parentKey = new DrawingItem(parent, "view").Key;
        var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        while (current.ParentView != null) { current = current.ParentView; var key = new DrawingItem(current, "view").Key; if (key == parentKey) return true; if (!visited.Add(key)) throw new InvalidOperationException("Cyclic native view dependency; deletion cannot be proven."); }
        return false;
    }
    private static DrawingItem[] Dependents(DrawingItem item, DrawingItem[] inventory)
    {
        if (item.Entity is not DrawingView view) return Array.Empty<DrawingItem>();
        var viewKey = item.Key;
        return inventory.Where(other => other.Key != viewKey && (other.Entity is DrawingView child ? Descendant(child, view) : ItemIntents(other).Any(i => i.Geometry is DrawingCurve curve && (new DrawingItem(curve.Parent, "view").Key == viewKey || Descendant(curve.Parent, view))))).ToArray();
    }
    private static DrawingItem[] ResolveTargets(DrawingDocument d, Sheet s, DrawingItem[] inventory, JArray targets)
    {
        var found = targets.Cast<JObject>().Select(t => ResolveItem(d, s, inventory, t)).ToArray();
        if (found.Select(i => i.Key).Distinct().Count() != found.Length) throw new ArgumentException("Multiple selectors resolve to the same item.");
        return found;
    }
    private static JArray OtherSheetDependents(DrawingDocument d, Sheet s, DrawingItem[] selected)
    {
        var views = selected.Where(i => i.Kind == "view").ToArray(); var result = new JArray();
        if (views.Length == 0) return result;
        foreach (var other in d.Sheets.Cast<Sheet>().Where(x => x.InternalName != s.InternalName))
        {
            RequireSupportedDeletion(other); var inventory = Inventory(other);
            foreach (var dependent in views.SelectMany(v => Dependents(v, inventory)).GroupBy(x => x.Key).Select(g => g.First()))
            {
                var target = dependent.Target(d, other); target["sheet"] = other.Name; result.Add(target);
            }
        }
        return result;
    }
    private static void RequireNoOtherSheetDependents(DrawingDocument d, Sheet s, DrawingItem[] selected)
    {
        if (OtherSheetDependents(d, s, selected).Count != 0) throw new ArgumentException("Dependent items exist on another sheet. Delete those exact items in separate calls, then preview this sheet again.");
    }
    private static InventorCommandResult DeleteItems(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var inventory = Inventory(s); DrawingItem[] selected;
        if (p.Value<bool?>("dry_run") != false)
        {
            var selector = (JObject)p["selector"]!; selected = inventory.Where(i => selector.Value<string>("kind") == "all" || i.Kind == selector.Value<string>("kind")).ToArray();
            if (selector["names"] is JArray names)
            {
                selected = names.Select(name => { var matches = selected.Where(i => i.Name == name.Value<string>()).ToArray(); if (matches.Length != 1) throw new ArgumentException("Preview name must resolve uniquely: " + name); return matches[0]; }).ToArray();
            }
            if (selector["region_mm"] is JObject region)
            {
                if (selected.Any(i => i.Box == null)) throw new NotSupportedException("Native bounds are unavailable for dimensions/symbols/balloons/centermarks. Use exact names/locators for these kinds; region preview supports views, notes and custom tables.");
                selected = selected.Where(i => Enumerable.Range(0, 2).All(axis => i.Box!["min"]![axis]!.Value<double>() <= region["max"]![axis]!.Value<double>() && i.Box!["max"]![axis]!.Value<double>() >= region["min"]![axis]!.Value<double>())).ToArray();
            }
            if (selected.Length > 100) throw new ArgumentException("Preview exceeds 100 items; narrow the selection.");
            if (selected.Any(i => i.Kind == "view")) RequireSupportedDeletion(s);
            return DrawingSupport.Success(ctx, new JObject { ["dry_run"] = true, ["count"] = selected.Length, ["items"] = new JArray(selected.Select(i => { var target = i.Target(d, s); target["box_mm"] = i.Box; target["dependents"] = new JArray(Dependents(i, inventory).Select(dep => dep.Target(d, s))); return target; })), ["cross_sheet_dependents"] = OtherSheetDependents(d, s, selected), ["deleted_count"] = 0, ["document_unchanged"] = true });
        }
        selected = ResolveTargets(d, s, inventory, (JArray)p["items"]!);
        if (selected.Any(i => i.Kind == "view")) RequireSupportedDeletion(s);
        RequireNoOtherSheetDependents(d, s, selected);
        DrawingTableEditPlan.RequireDependencies(selected.Select(i => i.Key).ToArray(), selected.SelectMany(i => Dependents(i, inventory)).Select(i => i.Key).Distinct().ToArray());
        // Rebind every explicit target and recompute dependents immediately before the transaction.
        inventory = Inventory(s); selected = ResolveTargets(d, s, inventory, (JArray)p["items"]!);
        RequireNoOtherSheetDependents(d, s, selected);
        DrawingTableEditPlan.RequireDependencies(selected.Select(i => i.Key).ToArray(), selected.SelectMany(i => Dependents(i, inventory)).Select(i => i.Key).Distinct().ToArray());
        var deleted = new JArray(selected.Select(i => i.Target(d, s)));
        var selectedKeys = selected.Select(i => i.Key).ToArray();
        var retainedKeys = inventory.Select(i => i.Key).Except(selectedKeys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return DrawingSupport.Atomic(ctx, d, "Delete drawing items", () =>
        {
            foreach (var item in selected.Where(i => i.Kind != "view")) item.Delete();
            // Delete leaf views before ancestors, so no native cascade expands the approved set.
            var views = selected.Where(i => i.Kind == "view").ToList();
            while (views.Count != 0) { var leaf = views.First(i => !views.Any(other => !other.Entity.Equals(i.Entity) && Descendant((DrawingView)other.Entity, (DrawingView)i.Entity))); leaf.Delete(); views.Remove(leaf); }
            var remaining = Inventory(s).Select(i => i.Key).ToArray();
            if (remaining.Intersect(selectedKeys, StringComparer.Ordinal).Any() || !retainedKeys.SequenceEqual(remaining.OrderBy(x => x, StringComparer.Ordinal))) throw new InvalidOperationException("Deletion readback differs from the exact approved item set.");
            return new JObject { ["deleted"] = true, ["deleted_count"] = selected.Length, ["count"] = selected.Length, ["items"] = deleted };
        });
    }
}
#endif
