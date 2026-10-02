using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

public static class DrawingPhase2Input
{
    public static string? Fields(string command) => command switch
    {
        "add_drawing_symbol" => "document sheet timeout_ms name kind definition position_mm prompts rotation_deg scale view intent style layer",
        "edit_drawing_annotation" => "document sheet timeout_ms items rebuild",
        "delete_drawing_items" => "document sheet timeout_ms selector items dry_run",
        "edit_drawing_table" => "document sheet timeout_ms name changes rebuild",
        "set_drawing_styles" => "document sheet timeout_ms changes",
        "edit_sheet" => "document sheet timeout_ms changes contents",
        _ => null
    };
    public static void Keys(JObject p, string allowed)
    {
        foreach (var key in p.Properties()) if (!allowed.Split(' ').Contains(key.Name)) throw new ArgumentException("Unknown parameter: " + key.Name);
    }
    public static JObject Object(JObject p, string key)
    {
        DrawingInput.Required(p, key);
        return p[key] as JObject ?? throw new ArgumentException(key + " must be an object.");
    }
    public static void Name(JObject p, params string[] keys)
    {
        foreach (var key in keys) if (DrawingInput.Present(p, key) && (p[key]!.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)p[key]) || ((string)p[key]!).Length > 128 || ((string)p[key]!).IndexOfAny(new[] {'\r','\n'}) >= 0)) throw new ArgumentException(key + " must be a nonempty single-line name of at most 128 characters.");
    }
    public static void Boolean(JObject p, params string[] keys)
    {
        foreach (var key in keys) if (DrawingInput.Present(p, key) && p[key]!.Type != JTokenType.Boolean) throw new ArgumentException(key + " must be boolean.");
    }
    public static void Integer(JObject p, string key, int min, int max)
    {
        if (DrawingInput.Present(p, key) && p[key]!.Type != JTokenType.Integer) throw new ArgumentException(key + " must be an integer.");
        DrawingInput.Range(p, key, min, max);
    }
    public static void Intent(JObject p)
    {
        var intent = Object(p, "intent"); Keys(intent, "model_edge model_point_mm occurrence_path point_intent"); DrawingInput.Any(intent, "model_edge", "model_point_mm");
        Name(intent, "model_edge", "occurrence_path", "point_intent");
        if (DrawingInput.Present(intent, "model_point_mm")) DrawingInput.Point(intent["model_point_mm"], 3);
        DrawingInput.Choice(intent, "point_intent", "start", "end", "mid", "center");
    }
    public static void Target(JObject p)
    {
        Keys(p, "kind name locator changes"); DrawingInput.Required(p, "kind"); Name(p, "kind", "name");
        DrawingInput.Choice(p, "kind", "view", "dimension", "balloon", "note", "symbol", "centermark", "table", "border", "title_block");
        if (DrawingInput.Present(p, "name") == DrawingInput.Present(p, "locator")) throw new ArgumentException("Specify exactly one name or locator.");
        if (DrawingInput.Present(p, "locator"))
        {
            var l = Object(p, "locator"); Keys(l, "document_id sheet_id kind reference_key"); DrawingInput.Required(l, "document_id", "sheet_id", "kind", "reference_key");
            if (l.Properties().Any(x => x.Value.Type != JTokenType.String)) throw new ArgumentException("Locator fields must be strings.");
            if ((string?)l["kind"] != (string?)p["kind"]) throw new ArgumentException("Locator kind differs from target kind.");
            var key = l.Value<string>("reference_key")!;
            if (key.Length > 4096) throw new ArgumentException("Locator reference key is too large.");
            try { if (Convert.FromBase64String(key).Length == 0) throw new ArgumentException("Empty reference key."); }
            catch (FormatException) { throw new ArgumentException("Locator reference key must be base64."); }
        }
    }
    public static JArray Targets(JObject p, string key)
    {
        if (p[key] is not JArray a || a.Count > 100) throw new ArgumentException(key + " must be an array of at most 100 exact targets.");
        var unique = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var token in a)
        {
            if (token is not JObject item) throw new ArgumentException("Each target must be an object.");
            Target(item);
            if (!unique.Add(DrawingInput.Signature(new JObject { ["kind"] = item["kind"]!.DeepClone(), ["name"] = item["name"]?.DeepClone(), ["locator"] = item["locator"]?.DeepClone() }))) throw new ArgumentException("Duplicate target.");
        }
        return a;
    }
    public static void Validate(string command, JObject p)
    {
        Boolean(p, "rebuild", "dry_run"); Name(p, "name", "sheet", "view", "style", "layer", "definition");
        if (DrawingInput.Present(p, "document") && (p["document"]!.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)p["document"]))) throw new ArgumentException("document must be a nonempty name or loaded path.");
        switch (command)
        {
            case "add_drawing_symbol":
                DrawingInput.Required(p, "name", "kind"); DrawingInput.Choice(p, "kind", "symbol", "centermark");
                DrawingInput.Range(p, "rotation_deg", -double.MaxValue, double.MaxValue);
                DrawingInput.Positive(p, "scale");
                if ((string?)p["kind"] == "symbol")
                {
                    DrawingInput.Required(p, "definition", "position_mm"); DrawingInput.Point(p["position_mm"], 2);
                    if (DrawingInput.Present(p, "view") || DrawingInput.Present(p, "intent")) { DrawingInput.Required(p, "view"); Intent(p); }
                    if (DrawingInput.Present(p, "prompts")) { var map = Object(p, "prompts"); if (map.Properties().Any(x => x.Value.Type != JTokenType.String)) throw new ArgumentException("Prompt values must be strings."); }
                }
                else
                {
                    DrawingInput.Required(p, "view"); Intent(p);
                    if (new[] { "definition", "position_mm", "prompts" }.Any(k => DrawingInput.Present(p, k)) || p.Value<double?>("rotation_deg") is double rotation && rotation != 0 || p.Value<double?>("scale") is double scale && scale != 1) throw new ArgumentException("Centermarks accept a circular intent, style and layer only.");
                }
                break;
            case "edit_drawing_annotation":
                var targets = Targets(p, "items"); if (targets.Count == 0) throw new ArgumentException("Supply at least one annotation.");
                foreach (JObject item in targets)
                {
                    DrawingInput.Choice(item, "kind", "dimension", "balloon", "note", "symbol");
                    var c = Object(item, "changes"); Keys(c, "text_position_mm text_override precision style layer leader_arrowhead");
                    DrawingInput.Any(c, "text_position_mm", "text_override", "precision", "style", "layer", "leader_arrowhead"); Name(c, "style", "layer");
                    if (DrawingInput.Present(c, "text_position_mm")) DrawingInput.Point(c["text_position_mm"], 2);
                    if (DrawingInput.Present(c, "text_override") && c["text_override"]!.Type != JTokenType.String) throw new ArgumentException("text_override must be a string.");
                    Integer(c, "precision", 0, 8); DrawingInput.Choice(c, "leader_arrowhead", "open", "closed", "filled", "none");
                    if ((string?)item["kind"] != "dimension" && DrawingInput.Present(c, "precision")) throw new ArgumentException("precision is available for dimensions only.");
                    if ((string?)item["kind"] == "dimension" && DrawingInput.Present(c, "leader_arrowhead")) throw new ArgumentException("leader_arrowhead is unavailable for dimensions; use a named dimension style.");
                }
                break;
            case "delete_drawing_items":
                DrawingInput.Required(p, "document", "sheet");
                if (p.Value<bool?>("dry_run") != false)
                {
                    if (DrawingInput.Present(p, "items")) throw new ArgumentException("Preview accepts selector only.");
                    var selector = Object(p, "selector"); Keys(selector, "kind names region_mm"); DrawingInput.Required(selector, "kind");
                    DrawingInput.Choice(selector, "kind", "all", "view", "dimension", "balloon", "note", "symbol", "centermark", "table");
                    if (DrawingInput.Present(selector, "names") && DrawingInput.Present(selector, "region_mm")) throw new ArgumentException("Select names or region_mm, not both.");
                    if (DrawingInput.Present(selector, "names"))
                    {
                        if (selector["names"] is not JArray names || names.Count == 0 || names.Count > 100) throw new ArgumentException("names must contain 1..100 exact names.");
                        foreach (var name in names) { var value = new JObject { ["name"] = name.DeepClone() }; DrawingInput.Required(value, "name"); Name(value, "name"); }
                        if (names.Select(x => x.Value<string>()).Distinct().Count() != names.Count) throw new ArgumentException("Duplicate name.");
                    }
                    if (DrawingInput.Present(selector, "region_mm")) { var region = Object(selector, "region_mm"); Keys(region, "min max"); DrawingInput.Required(region, "min", "max"); DrawingInput.Point(region["min"], 2); DrawingInput.Point(region["max"], 2); if (Enumerable.Range(0, 2).Any(i => region["min"]![i]!.Value<double>() >= region["max"]![i]!.Value<double>())) throw new ArgumentException("Region must have positive width and height."); }
                }
                else { if (DrawingInput.Present(p, "selector")) throw new ArgumentException("Execution requires exact items, not a selector."); var items = Targets(p, "items"); if (items.Count == 0) throw new ArgumentException("Execution requires at least one exact target."); foreach (JObject item in items) { Keys(item, "kind name locator"); DrawingInput.Choice(item, "kind", "view", "dimension", "balloon", "note", "symbol", "centermark", "table"); } }
                break;
            case "edit_drawing_table":
                DrawingInput.Required(p, "name"); var table = Object(p, "changes"); Keys(table, "cells insert_rows delete_rows column_widths_mm row_heights_mm title position_mm style header_height_mm");
                DrawingInput.Any(table, "cells", "insert_rows", "delete_rows", "column_widths_mm", "row_heights_mm", "title", "position_mm", "style", "header_height_mm"); Name(table, "style");
                if (DrawingInput.Present(table, "title") && table["title"]!.Type != JTokenType.String) throw new ArgumentException("title must be a string.");
                if (DrawingInput.Present(table, "position_mm")) DrawingInput.Point(table["position_mm"], 2);
                if (DrawingInput.Present(table, "header_height_mm")) { DrawingInput.Positive(table, "header_height_mm"); if (p.Value<bool?>("rebuild") != true) throw new ArgumentException("header_height_mm requires rebuild=true."); }
                foreach (var key in new[] { "column_widths_mm", "row_heights_mm" }) if (DrawingInput.Present(table, key)) { if (table[key] is not JArray sizes || sizes.Count > 500) throw new ArgumentException(key + " must be a bounded array."); foreach (var size in sizes) { var value = new JObject { ["size"] = size.DeepClone() }; DrawingInput.Required(value, "size"); DrawingInput.Positive(value, "size"); } }
                if (DrawingInput.Present(table, "cells")) { if (table["cells"] is not JArray cells || cells.Count == 0 || cells.Count > 100) throw new ArgumentException("cells must contain 1..100 edits."); foreach (var token in cells) { var cell = token as JObject ?? throw new ArgumentException("Cell must be an object."); Keys(cell, "row column text"); DrawingInput.Required(cell, "row", "column", "text"); Integer(cell, "row", 1, 500); Integer(cell, "column", 1, 50); if (cell["text"]!.Type != JTokenType.String) throw new ArgumentException("Cell text must be a string."); } }
                if (DrawingInput.Present(table, "delete_rows")) { if (table["delete_rows"] is not JArray rows || rows.Count == 0 || rows.Count > 500) throw new ArgumentException("delete_rows must contain 1..500 indices."); foreach (var row in rows) { var value = new JObject { ["row"] = row.DeepClone() }; DrawingInput.Required(value, "row"); Integer(value, "row", 1, 500); } if (rows.Distinct(JToken.EqualityComparer).Count() != rows.Count) throw new ArgumentException("Duplicate row deletion."); }
                if (DrawingInput.Present(table, "insert_rows")) { if (table["insert_rows"] is not JArray inserts || inserts.Count == 0 || inserts.Count > 100) throw new ArgumentException("insert_rows must be an array."); foreach (var token in inserts) { var insert = token as JObject ?? throw new ArgumentException("Insert must be an object."); Keys(insert, "index rows"); DrawingInput.Required(insert, "index", "rows"); Integer(insert, "index", 1, 501); if (insert["rows"] is not JArray rows || rows.Count == 0 || rows.Count > 500 || rows.Any(r => r is not JArray cells || cells.Count == 0 || cells.Count > 50 || cells.Any(c => c.Type != JTokenType.String))) throw new ArgumentException("Inserted rows require bounded arrays of strings."); } }
                break;
            case "set_drawing_styles":
                var styles = Object(p, "changes"); Keys(styles, "dimension_styles text_styles layers object_defaults"); DrawingInput.Any(styles, "dimension_styles", "text_styles", "layers", "object_defaults");
                foreach (var group in styles.Properties())
                {
                    if (group.Value is not JArray entries || entries.Count == 0 || entries.Count > 100) throw new ArgumentException(group.Name + " must contain 1..100 named changes.");
                    var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    foreach (var token in entries)
                    {
                        var entry = token as JObject ?? throw new ArgumentException("Style change must be an object.");
                        var keys = group.Name switch { "dimension_styles" => "name linear_precision units display_format text_style", "text_styles" => "name font size_mm bold italic", "layers" => "name color_rgb weight_mm line_type", _ => "kind style layer" }; Keys(entry, keys);
                        var identity = group.Name == "object_defaults" ? "kind" : "name"; DrawingInput.Required(entry, identity); Name(entry, identity, "text_style", "style", "layer");
                        if (!names.Add(entry.Value<string>(identity)!)) throw new ArgumentException("Duplicate style/default change.");
                        DrawingInput.Any(entry, keys.Split(' ').Where(k => k != identity).ToArray());
                        Integer(entry, "linear_precision", 0, 8); DrawingInput.Positive(entry, "size_mm", "weight_mm"); Boolean(entry, "bold", "italic");
                        if (DrawingInput.Present(entry, "font") && (entry["font"]!.Type != JTokenType.String || string.IsNullOrWhiteSpace(entry.Value<string>("font")))) throw new ArgumentException("font must be a nonempty string.");
                        DrawingInput.Choice(entry, "units", "mm", "cm", "m", "inch"); DrawingInput.Choice(entry, "display_format", "decimal", "fractional", "fractional_horizontal", "fractional_diagonal");
                        if (DrawingInput.Present(entry, "linear_precision") && DrawingInput.Present(entry, "display_format") && entry.Value<string>("display_format") != "decimal") throw new ArgumentException("linear_precision is a decimal-place count; fractional formats must omit it.");
                        DrawingInput.Choice(entry, "line_type", "continuous", "dashed", "dotted", "dash_dotted", "hidden");
                        if (DrawingInput.Present(entry, "color_rgb")) { if (entry["color_rgb"] is not JArray rgb || rgb.Count != 3) throw new ArgumentException("color_rgb needs three integers."); foreach (var component in rgb) { var value = new JObject { ["component"] = component.DeepClone() }; DrawingInput.Required(value, "component"); Integer(value, "component", 0, 255); } }
                        if (group.Name == "object_defaults") DrawingInput.Choice(entry, "kind", "linear_dimension", "angular_dimension", "diameter_dimension", "radial_dimension", "general_note", "leader_note", "symbol", "centermark", "table");
                    }
                }
                break;
            case "edit_sheet":
                DrawingInput.Required(p, "sheet"); var sheet = Object(p, "changes"); Keys(sheet, "name code index active delete width_mm height_mm orientation"); Name(sheet, "name", "code"); Boolean(sheet, "active", "delete"); Integer(sheet, "index", 1, int.MaxValue); DrawingInput.Positive(sheet, "width_mm", "height_mm"); DrawingInput.Choice(sheet, "orientation", "landscape", "portrait");
                var actions = new[] { new[] { "name", "code" }, new[] { "index" }, new[] { "active" }, new[] { "delete" }, new[] { "width_mm", "height_mm", "orientation" } }.Count(keys => keys.Any(k => DrawingInput.Present(sheet, k)));
                if (actions != 1) throw new ArgumentException("Choose exactly one lifecycle action or size change.");
                if (DrawingInput.Present(sheet, "active") && !sheet.Value<bool>("active") || DrawingInput.Present(sheet, "delete") && !sheet.Value<bool>("delete")) throw new ArgumentException("active/delete must be true when supplied.");
                if (DrawingInput.Present(sheet, "width_mm") != DrawingInput.Present(sheet, "height_mm")) throw new ArgumentException("Supply both width_mm and height_mm.");
                if (DrawingInput.Present(sheet, "width_mm") && DrawingInput.Present(sheet, "orientation") && (sheet.Value<string>("orientation") == "landscape" ? sheet.Value<double>("width_mm") < sheet.Value<double>("height_mm") : sheet.Value<double>("width_mm") > sheet.Value<double>("height_mm"))) throw new ArgumentException("Custom dimensions contradict the requested orientation.");
                if (sheet.Value<bool?>("delete") == true) { DrawingInput.Required(p, "document", "contents"); foreach (JObject item in Targets(p, "contents")) Keys(item, "kind name locator"); }
                else if (DrawingInput.Present(p, "contents")) throw new ArgumentException("contents applies only to sheet deletion.");
                break;
        }
    }
}
