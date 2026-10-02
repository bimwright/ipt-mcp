using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

public static class DrawingPhase3Input
{
    public static string? Fields(string command) => command switch
    {
        "find_view_geometry" => "document sheet timeout_ms view model_point_mm model_edge occurrence_path region_mm kind visible_only tolerance_mm max_items offset refresh",
        "sketch_on_view" => "document sheet timeout_ms name view space entities layer color_rgb weight_mm",
        "hide_view_edges" => "document sheet timeout_ms view occurrences max_model_size_mm dry_run",
        "create_design_view" => "document timeout_ms name source occurrence_visibility appearance activate",
        _ => null
    };

    public static void Validate(string command, JObject p)
    {
        DrawingPhase2Input.Keys(p, Fields(command) ?? throw new ArgumentException("Unknown Phase 3 command."));
        Finite(p);
        DrawingPhase2Input.Name(p, "name", "view", "layer", "source", "kind", "space");
        if (DrawingInput.Present(p, "document") && (p["document"]!.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)p["document"]))) throw new ArgumentException("document must be a loaded name or path.");
        if (DrawingInput.Present(p, "sheet") && (p["sheet"]!.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)p["sheet"]))) throw new ArgumentException("sheet must be an exact name or code.");
        DrawingPhase2Input.Integer(p, "timeout_ms", 1000, 600000);
        switch (command)
        {
            case "find_view_geometry":
                DrawingInput.Required(p, "view");
                var selectors = new[] { "model_point_mm", "model_edge", "region_mm" }.Count(k => DrawingInput.Present(p, k));
                if (selectors > 1 || selectors == 0 && !DrawingInput.Present(p, "occurrence_path")) throw new ArgumentException("Supply one model_point_mm, model_edge or region_mm selector, or an occurrence_path alone.");
                foreach (var key in new[] { "model_edge", "occurrence_path" }) if (DrawingInput.Present(p, key) && (p[key]!.Type != JTokenType.String || string.IsNullOrWhiteSpace(p.Value<string>(key)))) throw new ArgumentException(key + " must be a nonempty string.");
                if (DrawingInput.Present(p, "model_point_mm")) DrawingInput.Point(p["model_point_mm"], 3);
                if (DrawingInput.Present(p, "region_mm")) Region(DrawingPhase2Input.Object(p, "region_mm"));
                DrawingInput.Choice(p, "kind", "line", "arc", "circle", "all");
                DrawingInput.Positive(p, "tolerance_mm");
                DrawingPhase2Input.Integer(p, "max_items", 1, 1000); DrawingPhase2Input.Integer(p, "offset", 0, int.MaxValue);
                DrawingPhase2Input.Boolean(p, "visible_only", "refresh");
                break;
            case "sketch_on_view":
                DrawingInput.Required(p, "name", "entities"); DrawingInput.Choice(p, "space", "sheet", "view_local");
                if (p.Value<string>("space") == "view_local") DrawingInput.Required(p, "view");
                DrawingInput.Positive(p, "weight_mm"); Rgb(p);
                if (p["entities"] is not JArray entities || entities.Count == 0 || entities.Count > 500) throw new ArgumentException("entities must contain 1..500 sketch entities.");
                foreach (var token in entities)
                {
                    if (token is not JObject entity || entity.Count != 1) throw new ArgumentException("Each entity must contain exactly one line, circle, arc or text object.");
                    var property = entity.Properties().Single();
                    var e = property.Value as JObject ?? throw new ArgumentException("Sketch entity data must be an object.");
                    switch (property.Name)
                    {
                        case "line":
                            DrawingPhase2Input.Keys(e, "from to"); DrawingInput.Required(e, "from", "to"); DrawingInput.Point(e["from"], 2); DrawingInput.Point(e["to"], 2);
                            if (JToken.DeepEquals(e["from"], e["to"]) || Enumerable.Range(0, 2).All(i => Math.Abs(e["from"]![i]!.Value<double>() - e["to"]![i]!.Value<double>()) < 1e-7)) throw new ArgumentException("Line endpoints must differ.");
                            break;
                        case "circle": case "arc":
                            DrawingPhase2Input.Keys(e, property.Name == "circle" ? "center radius_mm" : "center radius_mm start_deg sweep_deg");
                            DrawingInput.Required(e, "center", "radius_mm"); DrawingInput.Point(e["center"], 2); DrawingInput.Positive(e, "radius_mm");
                            if (property.Name == "arc")
                            {
                                DrawingInput.Required(e, "start_deg", "sweep_deg"); DrawingInput.Range(e, "start_deg", -double.MaxValue, double.MaxValue); DrawingInput.Range(e, "sweep_deg", -360, 360);
                                if (Math.Abs(e.Value<double>("sweep_deg")) < 1e-7 || Math.Abs(e.Value<double>("sweep_deg")) >= 360) throw new ArgumentException("Arc sweep must be nonzero and below 360 degrees; use circle for a full revolution.");
                            }
                            break;
                        case "text":
                            DrawingPhase2Input.Keys(e, "text position font_size_mm rotation_deg"); DrawingInput.Required(e, "text", "position"); DrawingInput.Point(e["position"], 2);
                            if (e["text"]!.Type != JTokenType.String || string.IsNullOrEmpty(e.Value<string>("text"))) throw new ArgumentException("Sketch text must be a nonempty literal string.");
                            DrawingInput.Positive(e, "font_size_mm"); DrawingInput.Range(e, "rotation_deg", -double.MaxValue, double.MaxValue);
                            break;
                        default: throw new ArgumentException("Unknown sketch entity: " + property.Name);
                    }
                }
                break;
            case "hide_view_edges":
                DrawingInput.Required(p, "document", "sheet", "view"); DrawingInput.Any(p, "occurrences", "max_model_size_mm");
                DrawingInput.Positive(p, "max_model_size_mm"); DrawingPhase2Input.Boolean(p, "dry_run");
                if (DrawingInput.Present(p, "occurrences") && !OccurrenceSelectorSpec.TryParse(p["occurrences"], "occurrences", out _, out var error)) throw new ArgumentException(error);
                break;
            case "create_design_view":
                DrawingInput.Required(p, "name"); DrawingPhase2Input.Boolean(p, "activate");
                foreach (var key in new[] { "occurrence_visibility", "appearance" })
                {
                    if (!DrawingInput.Present(p, key)) continue;
                    if (p[key] is not JArray entries || entries.Count == 0 || entries.Count > 500) throw new ArgumentException(key + " must contain 1..500 exact occurrence settings.");
                    var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var token in entries)
                    {
                        var entry = token as JObject ?? throw new ArgumentException("Occurrence setting must be an object.");
                        DrawingPhase2Input.Keys(entry, key == "appearance" ? "occurrence_path asset" : "occurrence_path visible");
                        DrawingInput.Required(entry, "occurrence_path", key == "appearance" ? "asset" : "visible"); DrawingPhase2Input.Name(entry, "asset");
                        if (entry["occurrence_path"]!.Type != JTokenType.String || string.IsNullOrWhiteSpace(entry.Value<string>("occurrence_path"))) throw new ArgumentException("occurrence_path must be an exact nonempty path.");
                        if (!paths.Add(entry.Value<string>("occurrence_path")!)) throw new ArgumentException("Duplicate occurrence setting.");
                        DrawingPhase2Input.Boolean(entry, "visible");
                    }
                }
                break;
        }
    }

    public static void GeometryIntent(JObject p)
    {
        DrawingPhase2Input.Keys(p, "model_point_mm model_edge occurrence_path point_intent geometry_id revision");
        var selectors = new[] { "model_point_mm", "model_edge", "geometry_id" }.Count(k => DrawingInput.Present(p, k));
        if (selectors != 1) throw new ArgumentException("Intent requires exactly one model_point_mm, model_edge or geometry_id.");
        if (DrawingInput.Present(p, "model_point_mm")) DrawingInput.Point(p["model_point_mm"], 3);
        if (DrawingInput.Present(p, "geometry_id")) { DrawingInput.Required(p, "revision"); if (DrawingInput.Present(p, "occurrence_path")) throw new ArgumentException("A geometry_id already identifies its occurrence within the queried view."); }
        else if (DrawingInput.Present(p, "revision")) throw new ArgumentException("revision requires geometry_id.");
        foreach (var key in new[] { "model_edge", "occurrence_path", "point_intent", "geometry_id", "revision" }) if (DrawingInput.Present(p, key) && (p[key]!.Type != JTokenType.String || string.IsNullOrWhiteSpace(p.Value<string>(key)))) throw new ArgumentException(key + " must be a nonempty string.");
        DrawingInput.Choice(p, "point_intent", "start", "end", "mid", "center"); Finite(p);
    }

    private static void Region(JObject region)
    {
        DrawingPhase2Input.Keys(region, "min max"); DrawingInput.Required(region, "min", "max"); DrawingInput.Point(region["min"], 2); DrawingInput.Point(region["max"], 2);
        if (Enumerable.Range(0, 2).Any(i => region["min"]![i]!.Value<double>() >= region["max"]![i]!.Value<double>())) throw new ArgumentException("Region must have positive width and height.");
    }
    private static void Rgb(JObject p)
    {
        if (!DrawingInput.Present(p, "color_rgb")) return;
        if (p["color_rgb"] is not JArray rgb || rgb.Count != 3 || rgb.Any(c => c.Type != JTokenType.Integer || c.Value<int>() < 0 || c.Value<int>() > 255)) throw new ArgumentException("color_rgb must be three integers in 0..255.");
    }
    private static void Finite(JToken value)
    {
        if (value.Type == JTokenType.Float && (double.IsNaN(value.Value<double>()) || double.IsInfinity(value.Value<double>()))) throw new ArgumentException("Numbers must be finite.");
        if (value is JContainer container) foreach (var child in container.Children()) Finite(child);
    }
}
