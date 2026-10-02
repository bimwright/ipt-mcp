#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal sealed class DrawingIntent
{
    internal DrawingCurve Curve { get; init; } = null!;
    internal GeometryIntent Intent { get; init; } = null!;
    internal Point2d Point { get; init; } = null!;
    internal JObject Locator { get; init; } = null!;
}

/// <summary>Resolves actual model-backed curve intents, never unattached sheet coordinates.</summary>
internal static class DrawingGeometry
{
    internal static ComponentOccurrence Occurrence(AssemblyDocument model, string path)
    {
        var parts = path.Split('/'); ComponentOccurrence? current = null;
        foreach (var segment in parts)
        {
            var candidates = (current == null ? model.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>() : current.SubOccurrences.Cast<ComponentOccurrence>()).Where(x => x.Name == segment).ToArray();
            if (candidates.Length != 1) throw new ArgumentException("Occurrence path does not resolve uniquely: " + path);
            current = candidates[0];
        }
        return current ?? throw new ArgumentException("Empty occurrence path.");
    }
    internal static DrawingCurvesEnumerator Curves(DrawingView view, string? occurrencePath)
    {
        if (occurrencePath == null) return view.DrawingCurves[Type.Missing];
        if (view.ReferencedDocumentDescriptor.ReferencedDocument is not AssemblyDocument assembly) throw new ArgumentException("occurrence_path requires an assembly-backed view.");
        return view.DrawingCurves[Occurrence(assembly, occurrencePath)];
    }
    internal static DrawingIntent Resolve(Sheet sheet, DrawingView view, JObject locator, double toleranceMm)
    {
        var occurrencePath = (string?)locator["occurrence_path"];
        var curves = Curves(view, occurrencePath).Cast<DrawingCurve>().ToArray();
        var edgeRef = (string?)locator["model_edge"];
        if (edgeRef != null)
        {
            object geometry;
            var model = view.ReferencedDocumentDescriptor.ReferencedDocument;
            if (model is PartDocument part) geometry = EntityResolver.ResolveEdge(part.ComponentDefinition, edgeRef);
            else if (model is AssemblyDocument assembly && occurrencePath != null)
            {
                var occurrence = Occurrence(assembly, occurrencePath);
                if (occurrence.Definition is not PartComponentDefinition definition) throw new ArgumentException("model_edge must resolve to a part occurrence.");
                occurrence.CreateGeometryProxy(EntityResolver.ResolveEdge(definition, edgeRef), out geometry);
            }
            else throw new ArgumentException("model_edge on an assembly requires occurrence_path.");
            curves = view.DrawingCurves[geometry].Cast<DrawingCurve>().ToArray();
        }
        var pointIntent = (string?)locator["point_intent"];
        if (pointIntent != null && !new[] { "start", "end", "mid", "center" }.Contains(pointIntent)) throw new ArgumentException("point_intent must be start|end|mid|center.");
        var candidates = new List<(DrawingCurve curve, PointIntentEnum intent, Point2d point, string identity)>();
        if (locator["model_point_mm"] is JArray modelPoint)
        {
            DrawingInput.Point(modelPoint, 3); var wanted = new[] { (double)modelPoint[0] / 10, (double)modelPoint[1] / 10, (double)modelPoint[2] / 10 };
            foreach (var c in curves)
            {
                var geometry = c.ModelGeometry;
                foreach (var intent in pointIntent == null ? new[] { "start", "end", "mid", "center" } : new[] { pointIntent })
                {
                    Point? model = null; Point2d? projected = null;
                    try
                    {
                        if (intent == "start" || intent == "end")
                        {
                            model = geometry switch { Edge e => intent == "start" ? e.StartVertex.Point : e.StopVertex.Point, EdgeProxy e => intent == "start" ? e.StartVertex.Point : e.StopVertex.Point, _ => null };
                            // Topological and projected edge directions need not agree.
                            if (model != null) { var mapped = view.ModelToSheetSpace(model); projected = new[] { c.StartPoint, c.EndPoint }.Where(x => x != null).OrderBy(x => x.DistanceTo(mapped)).FirstOrDefault(); if (projected != null && projected.DistanceTo(mapped) > toleranceMm / 10 * view.Scale) continue; }
                        }
                        else if (intent == "center")
                        {
                            object? g = geometry switch { Edge e => e.Geometry, EdgeProxy e => e.Geometry, _ => null };
                            model = g switch { Circle circle => circle.Center, Arc3d arc => arc.Center, _ => null }; projected = c.CenterPoint;
                        }
                        else if (c.CurveType == CurveTypeEnum.kLineSegmentCurve)
                        {
                            var start = geometry switch { Edge e => e.StartVertex.Point, EdgeProxy e => e.StartVertex.Point, _ => null }; var end = geometry switch { Edge e => e.StopVertex.Point, EdgeProxy e => e.StopVertex.Point, _ => null };
                            if (start != null && end != null) { model = ((Application)sheet.Application).TransientGeometry.CreatePoint((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2); projected = c.MidPoint; }
                        }
                    }
                    catch { continue; }
                    if (model == null || projected == null) continue;
                    var distance = Math.Sqrt(Math.Pow(model.X - wanted[0], 2) + Math.Pow(model.Y - wanted[1], 2) + Math.Pow(model.Z - wanted[2], 2));
                    if (distance > toleranceMm / 10) continue;
                    var pointEnum = intent switch { "center" => PointIntentEnum.kCenterPointIntent, "mid" => PointIntentEnum.kMidPointIntent, _ => projected.DistanceTo(c.StartPoint) < projected.DistanceTo(c.EndPoint) ? PointIntentEnum.kStartPointIntent : PointIntentEnum.kEndPointIntent };
                    // Multiple edges sharing the same vertex represent the same attached point.
                    var identity = intent == "start" || intent == "end" ? $"vertex:{model.X:R},{model.Y:R},{model.Z:R}" : "curve:" + Array.IndexOf(curves, c) + ":" + intent;
                    candidates.Add((c, pointEnum, projected, identity));
                }
            }
        }
        else
        {
            if (edgeRef == null) throw new ArgumentException("Intent requires model_point_mm or model_edge.");
            foreach (var c in curves)
            {
                var intent = pointIntent switch { "start" => PointIntentEnum.kStartPointIntent, "end" => PointIntentEnum.kEndPointIntent, "center" => PointIntentEnum.kCenterPointIntent, _ => PointIntentEnum.kMidPointIntent };
                var point = pointIntent switch { "start" => c.StartPoint, "end" => c.EndPoint, "center" => c.CenterPoint, _ => c.MidPoint };
                if (point != null) candidates.Add((c, intent, point, "curve:" + Array.IndexOf(curves, c)));
            }
        }
        var unique = candidates.GroupBy(x => x.identity).Select(x => x.First()).ToArray();
        if (unique.Length != 1) throw new ArgumentException("Geometry intent resolved to " + unique.Length + " distinct targets; supply an exact model_edge/occurrence_path/point_intent.");
        var found = unique[0]; return new DrawingIntent { Curve = found.curve, Intent = sheet.CreateGeometryIntent(found.curve, found.intent), Point = found.point, Locator = (JObject)locator.DeepClone() };
    }
}
#endif
