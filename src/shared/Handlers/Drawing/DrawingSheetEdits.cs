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
    private static JObject SheetState(DrawingDocument d, Sheet s)
    {
        var state = DrawingSupport.SheetInfo(s);
        state["sheet_id"] = s.InternalName;
        state["index"] = Enumerable.Range(1, d.Sheets.Count).Single(i => d.Sheets[i].InternalName == s.InternalName);
        state["active"] = d.ActiveSheet.InternalName == s.InternalName;
        state["orientation"] = s.Orientation.ToString();
        return state;
    }
    private static InventorCommandResult EditSheet(InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var c = (JObject)p["changes"]!;
        if (c.Value<bool?>("active") == true)
        {
            s.Activate();
            var state = SheetState(d, s);
            if (!state.Value<bool>("active")) throw new InvalidOperationException("Sheet activation readback failed.");
            state["updated"] = true; state["count"] = 1;
            return DrawingSupport.Success(ctx, state);
        }
        var others = d.Sheets.Cast<Sheet>().Where(x => x.InternalName != s.InternalName).ToArray();
        foreach (var key in new[] { "name", "code" })
            if (DrawingInput.Present(c, key) && others.Any(x => x.Name == c.Value<string>(key) || DrawingSupport.Read(x.AttributeSets, "name") == c.Value<string>(key) || DrawingSupport.Read(x.AttributeSets, "code") == c.Value<string>(key))) throw new ArgumentException("Sheet name/code conflicts with another sheet.");
        if (c.Value<int?>("index") is int index && index > d.Sheets.Count) throw new ArgumentException("index exceeds the current sheet count.");
        if (c.Value<bool?>("delete") == true)
        {
            RequireSupportedDeletion(s);
            var inventory = Inventory(s, true);
            var selected = ResolveTargets(d, s, inventory, (JArray)p["contents"]!);
            RequireNoOtherSheetDependents(d, s, selected);
            DrawingTableEditPlan.RequireSheetDeletion(d.Sheets.Count, selected.Select(x => x.Key).ToArray(), inventory.Select(x => x.Key).ToArray());
            var id = s.InternalName; var name = s.Name; var contents = new JArray(inventory.Select(x => x.Target(d, s)));
            var retained = others.Select(x => x.InternalName).OrderBy(x => x).ToArray();
            return DrawingSupport.Atomic(ctx, d, "Delete drawing sheet", () =>
            {
                var fresh = Inventory(s, true);
                RequireNoOtherSheetDependents(d, s, fresh);
                DrawingTableEditPlan.RequireSheetDeletion(d.Sheets.Count, ResolveTargets(d, s, fresh, (JArray)p["contents"]!).Select(x => x.Key).ToArray(), fresh.Select(x => x.Key).ToArray());
                s.Delete();
                if (!retained.SequenceEqual(d.Sheets.Cast<Sheet>().Select(x => x.InternalName).OrderBy(x => x))) throw new InvalidOperationException("Sheet deletion changed the retained sheet set.");
                return new JObject { ["deleted"] = true, ["sheet"] = name, ["sheet_id"] = id, ["deleted_count"] = 1, ["count"] = 1, ["contents"] = contents, ["sheet_count"] = d.Sheets.Count };
            });
        }
        var resizing = new[] { "width_mm", "height_mm", "orientation" }.Any(k => DrawingInput.Present(c, k));
        var views = s.DrawingViews.Cast<DrawingView>().Select(v => new { View = v, X = v.Position.X, Y = v.Position.Y, Scale = v.Scale }).ToArray();
        if (resizing) DrawingSupport.RequireAnnotations(s);
        return DrawingSupport.Atomic(ctx, d, "Edit drawing sheet", () =>
        {
            if (DrawingInput.Present(c, "name")) { s.Name = c.Value<string>("name"); DrawingSupport.Write(s.AttributeSets, "name", c.Value<string>("name")!); }
            if (DrawingInput.Present(c, "code")) DrawingSupport.Write(s.AttributeSets, "code", c.Value<string>("code")!);
            if (c.Value<int?>("index") is int desired)
            {
                var sheets = Enumerable.Range(1, d.Sheets.Count).Select(i => d.Sheets[i]).ToArray(); var current = Array.FindIndex(sheets, x => x.InternalName == s.InternalName) + 1;
                if (current != desired) { var pane = d.BrowserPanes["Model"]; pane.Reorder(pane.GetBrowserNodeFromObject(sheets[desired - 1]), current > desired, pane.GetBrowserNodeFromObject(s)); }
            }
            if (resizing)
            {
                if (DrawingInput.Present(c, "orientation")) s.Orientation = c.Value<string>("orientation") == "portrait" ? PageOrientationTypeEnum.kPortraitPageOrientation : PageOrientationTypeEnum.kLandscapePageOrientation;
                if (DrawingInput.Present(c, "width_mm"))
                {
                    s.Size = DrawingSheetSizeEnum.kCustomDrawingSheetSize;
                    var width = c.Value<double>("width_mm") / 10; var height = c.Value<double>("height_mm") / 10;
                    // Set the shorter side first; crossing the current long side swaps
                    // native dimensions to preserve orientation during each setter.
                    if (width >= height) { s.Height = height; s.Width = width; }
                    else { s.Width = width; s.Height = height; }
                }
                d.Update();
                if (views.Any(v => Math.Abs(v.View.Position.X - v.X) > 1e-6 || Math.Abs(v.View.Position.Y - v.Y) > 1e-6 || Math.Abs(v.View.Scale - v.Scale) > 1e-9)) throw new InvalidOperationException("Sheet resize moved or rescaled model views.");
            }
            var actual = SheetState(d, s);
            if (DrawingInput.Present(c, "orientation") && s.Orientation != (c.Value<string>("orientation") == "portrait" ? PageOrientationTypeEnum.kPortraitPageOrientation : PageOrientationTypeEnum.kLandscapePageOrientation)) throw new InvalidOperationException("Sheet orientation readback differs from request.");
            if (DrawingInput.Present(c, "name") && DrawingSupport.Read(s.AttributeSets, "name") != c.Value<string>("name") || DrawingInput.Present(c, "code") && actual.Value<string>("code") != c.Value<string>("code") || c.Value<int?>("index") is int wanted && actual.Value<int>("index") != wanted || DrawingInput.Present(c, "width_mm") && (Math.Abs(s.Width * 10 - c.Value<double>("width_mm")) > 0.001 || Math.Abs(s.Height * 10 - c.Value<double>("height_mm")) > 0.001)) throw new InvalidOperationException("Sheet edit readback differs from request: " + actual.ToString(Newtonsoft.Json.Formatting.None) + " requested " + c.ToString(Newtonsoft.Json.Formatting.None));
            if (resizing)
            {
                var inventory = Inventory(s);
                actual["out_of_bounds"] = new JArray(inventory.Where(i => i.Box != null && (i.Box["min"]![0]!.Value<double>() < 0 || i.Box["min"]![1]!.Value<double>() < 0 || i.Box["max"]![0]!.Value<double>() > s.Width * 10 || i.Box["max"]![1]!.Value<double>() > s.Height * 10)).Select(i => i.Target(d, s)));
                actual["bounds_unavailable"] = new JArray(inventory.Where(i => i.Box == null).Select(i => i.Target(d, s)));
                actual["views_rescaled"] = false;
            }
            actual["updated"] = true; actual["count"] = 1;
            return actual;
        });
    }
}
#endif
