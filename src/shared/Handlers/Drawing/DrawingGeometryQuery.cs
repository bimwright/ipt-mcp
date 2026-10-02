#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingGeometry
{
    // Weak lifetime tags distinguish reopened documents. No native geometry is cached.
    private sealed class Lifetime { internal string Id { get; } = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<DrawingDocument, Lifetime> Lifetimes = new();
    internal sealed class Snapshot
    {
        internal DrawingCurve[] Curves { get; init; } = Array.Empty<DrawingCurve>();
        internal JObject[] Rows { get; init; } = Array.Empty<JObject>();
        internal string Revision { get; init; } = "";
    }
    internal static string Hash(string input)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLowerInvariant();
    }
    internal static JToken PointInfo(Point2d? point) => point == null ? JValue.CreateNull() : new JArray(point.X * 10, point.Y * 10);
    internal static JObject CurveBox(DrawingCurve curve)
    {
        var box = curve.Evaluator2D.RangeBox;
        return new JObject { ["min"] = PointInfo(box.MinPoint), ["max"] = PointInfo(box.MaxPoint) };
    }
    internal static double CurveLengthMm(DrawingCurve curve)
    {
        curve.Evaluator2D.GetParamExtents(out var start, out var end);
        curve.Evaluator2D.GetLengthAtParam(start, end, out var length);
        return length * 10;
    }
    private static JObject? ModelReference(DrawingCurve curve)
    {
        try
        {
            var geometry = curve.ModelGeometry; byte[] key = Array.Empty<byte>(); string? path = null;
            if (geometry is Edge edge) edge.GetReferenceKey(ref key, 0);
            else if (geometry is EdgeProxy proxy)
            {
                proxy.GetReferenceKey(ref key, 0);
                var parts = new List<string>(); var occurrence = proxy.ContainingOccurrence;
                while (occurrence != null && parts.Count < 64) { parts.Add(occurrence.Name); occurrence = occurrence.ParentOccurrence; }
                parts.Reverse(); path = string.Join("/", parts);
            }
            else return null;
            return new JObject { ["reference_key"] = Convert.ToBase64String(key), ["occurrence_path"] = path };
        }
        catch { return null; }
    }
    internal static Snapshot Read(DrawingDocument document, Sheet sheet, DrawingView view)
    {
        DrawingSupport.RequireAnnotations(sheet);
        if (view.Suppressed || !view.UpToDate) throw new ArgumentException("View geometry is unavailable or out of date. Update/activate the drawing before querying geometry.");
        var collection = Curves(view, null);
        if (collection.Count > 50000) throw new ArgumentException("View exceeds the 50000-curve geometry snapshot limit.");
        var curves = collection.Cast<DrawingCurve>().ToArray();
        var rows = curves.Select((curve, index) =>
        {
            var kind = curve.ProjectedCurveType switch { Curve2dTypeEnum.kLineSegmentCurve2d => "line", Curve2dTypeEnum.kCircularArcCurve2d => "arc", Curve2dTypeEnum.kCircleCurve2d => "circle", _ => "other" };
            var segments = curve.Segments.Cast<DrawingCurveSegment>().ToArray();
            double? radius = null;
            foreach (var segment in segments) { if (segment.Geometry is Circle2d circle) radius = circle.Radius * 10; else if (segment.Geometry is Arc2d arc) radius = arc.Radius * 10; if (radius.HasValue) break; }
            var model = ModelReference(curve);
            return new JObject {
                ["geometry_id"] = "curve:" + (index + 1).ToString(CultureInfo.InvariantCulture), ["kind"] = kind,
                ["start_mm"] = PointInfo(curve.StartPoint), ["end_mm"] = PointInfo(curve.EndPoint),
                ["mid_mm"] = PointInfo(curve.MidPoint), ["center_mm"] = PointInfo(curve.CenterPoint), ["radius_mm"] = radius,
                ["box_mm"] = CurveBox(curve), ["projected_length_mm"] = CurveLengthMm(curve),
                ["visible"] = segments.Any(s => s.Visible), ["segment_visibility"] = new JArray(segments.Select(s => s.Visible)),
                ["model_reference"] = model, ["occurrence_path"] = model?["occurrence_path"]?.DeepClone(),
                ["intent_supported"] = model != null
            };
        }).ToArray();
        byte[] viewKey = Array.Empty<byte>(); view.GetReferenceKey(ref viewKey, 0);
        var modelDocument = view.ReferencedDocumentDescriptor.ReferencedDocument;
        var state = new JObject {
            ["lifetime"] = Lifetimes.GetValue(document, _ => new Lifetime()).Id,
            ["document"] = document.InternalName, ["sheet"] = sheet.InternalName, ["view_key"] = Convert.ToBase64String(viewKey),
            ["database_revision"] = document.DatabaseRevisionId, ["dirty"] = document.Dirty,
            ["model_revision"] = modelDocument.DatabaseRevisionId, ["model_dirty"] = modelDocument.Dirty,
            ["view"] = DrawingSupport.ViewInfo(view), ["curves"] = new JArray(rows.Select(r => r.DeepClone()))
        };
        return new Snapshot { Curves = curves, Rows = rows, Revision = Hash(state.ToString(Formatting.None)) };
    }
    private static DrawingIntent ResolveGeometryId(Sheet sheet, DrawingView view, JObject locator)
    {
        var snapshot = Read((DrawingDocument)sheet.Parent, sheet, view);
        if (snapshot.Revision != locator.Value<string>("revision")) throw new ArgumentException("Stale geometry revision. Run find_view_geometry again before creating an annotation.");
        var index = Array.FindIndex(snapshot.Rows, row => row.Value<string>("geometry_id") == locator.Value<string>("geometry_id"));
        if (index < 0) throw new ArgumentException("geometry_id does not belong to this live view.");
        if (!snapshot.Rows[index].Value<bool>("intent_supported")) throw new NotSupportedException("This curve has no resolvable model edge reference for an attached intent.");
        var curve = snapshot.Curves[index]; var pointIntent = locator.Value<string>("point_intent") ?? (curve.ProjectedCurveType == Curve2dTypeEnum.kCircleCurve2d ? "center" : "mid");
        var point = pointIntent switch { "start" => curve.StartPoint, "end" => curve.EndPoint, "center" => curve.CenterPoint, _ => curve.MidPoint };
        if (point == null) throw new ArgumentException("Requested point intent is unavailable on this geometry; circular curves require center.");
        var intent = pointIntent switch { "start" => PointIntentEnum.kStartPointIntent, "end" => PointIntentEnum.kEndPointIntent, "center" => PointIntentEnum.kCenterPointIntent, _ => PointIntentEnum.kMidPointIntent };
        return new DrawingIntent { Curve = curve, Point = point, Intent = sheet.CreateGeometryIntent(curve, intent), Locator = (JObject)locator.DeepClone() };
    }
}

internal static partial class DrawingOperations
{
    private static InventorCommandResult FindGeometry(Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext ctx, DrawingDocument d, Sheet s, JObject p)
    {
        var view = DrawingSupport.View(s, p.Value<string>("view"));
        var snapshot = DrawingGeometry.Read(d, s, view);
        DrawingCurve[]? candidates = null;
        if (DrawingInput.Present(p, "model_point_mm"))
        {
            var intent = new JObject { ["model_point_mm"] = p["model_point_mm"]!.DeepClone(), ["occurrence_path"] = p["occurrence_path"]?.DeepClone() };
            candidates = new[] { DrawingGeometry.Resolve(s, view, intent, p.Value<double?>("tolerance_mm") ?? 0.5).Curve };
        }
        else if (DrawingInput.Present(p, "model_edge") || DrawingInput.Present(p, "occurrence_path")) candidates = DrawingGeometry.Candidates(view, p);
        var rows = snapshot.Rows.Where((row, index) =>
            (candidates == null || candidates.Any(c => c.Equals(snapshot.Curves[index]))) &&
            (p.Value<string>("kind") == null || p.Value<string>("kind") == "all" || p.Value<string>("kind") == row.Value<string>("kind")) &&
            (p.Value<bool?>("visible_only") == false || row.Value<bool>("visible")) &&
            (p["region_mm"] is not JObject region || Enumerable.Range(0, 2).All(axis => row["box_mm"]!["min"]![axis]!.Value<double>() <= region["max"]![axis]!.Value<double>() && row["box_mm"]!["max"]![axis]!.Value<double>() >= region["min"]![axis]!.Value<double>()))).ToArray();
        var page = Page(rows, p.Value<int?>("max_items") ?? 100, p.Value<int?>("offset") ?? 0);
        return DrawingSupport.Success(ctx, new JObject { ["view"] = view.Name, ["sheet"] = s.Name, ["revision"] = snapshot.Revision,
            ["geometry"] = page, ["count"] = rows.Length, ["cache_hit"] = false, ["resolution"] = "fresh_native", ["document_unchanged"] = true });
    }
}
#endif
