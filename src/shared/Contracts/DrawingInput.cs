using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Wire validation shared by the MCP boundary and the add-in. No COM calls.</summary>
public static class DrawingInput
{
    public static readonly string[] Commands = {
        "get_drawing_info", "new_drawing", "add_sheet", "set_title_block", "add_drawing_view",
        "add_section_view", "edit_drawing_view", "add_drawing_dimension", "add_balloon",
        "export_drawing", "capture_sheet"
    };

    public static JObject Normalize(string command, JObject input)
    {
        var p = (JObject)input.DeepClone();
        void Default(string key, JToken value) { if (!Present(p, key)) p[key] = value; }
        switch (command)
        {
            case "get_drawing_info": Default("include", "summary"); Default("max_items", 200); Default("offset", 0); Default("references", true); break;
            case "new_drawing": Default("projection", "template"); Default("visible", true); break;
            case "add_sheet": Default("size", "A1"); Default("orientation", "landscape"); break;
            case "add_drawing_view": Default("orientation", "front"); Default("scale", 1); Default("style", "hidden_line_removed"); Default("design_view_associative", true); break;
            case "add_section_view": Default("direction", "positive"); Default("rotation_deg", 0); break;
            case "edit_drawing_view": Default("rebuild", false); break;
            case "add_balloon": Default("mode", "symbol"); Default("allow_model_bom_change", false); break;
            case "export_drawing": Default("all_sheets", false); Default("dpi", 300); Default("overwrite_existing", false); Default("silent", true); break;
            case "capture_sheet": Default("width", 1600); Default("height", 1100); Default("inline", false); break;
        }
        if (p["items"] is JArray items) foreach (var item in items.OfType<JObject>()) if (!Present(item, "tolerance_mm")) item["tolerance_mm"] = 0.5;
        return p;
    }

    public static void Validate(string command, JObject p)
    {
        if (!Commands.Contains(command)) throw new ArgumentException("Unknown drawing command.");
        var fields = command switch
        {
            "get_drawing_info" => "document sheet timeout_ms include max_items offset references",
            "new_drawing" => "timeout_ms name template projection annotation_defaults visible",
            "add_sheet" => "document timeout_ms name code size orientation width_mm height_mm border title_block prompts",
            "set_title_block" => "document sheet timeout_ms title_block prompts iproperties",
            "add_drawing_view" => "document sheet timeout_ms name kind position_mm model parent_view orientation scale style eye_direction up_direction detail_region_mm design_view design_view_associative label reference_display margin_mm hidden_line_all_bodies",
            "add_section_view" => "document sheet timeout_ms name parent_view cut_line_mm position_mm direction depth_mm scale style rotation_deg label inherit_3d",
            "edit_drawing_view" => "document sheet timeout_ms view position_mm align scale style label design_view suppressed rotation_deg crop rebuild reference_display margin_mm hidden_line_all_bodies",
            "add_drawing_dimension" => "document sheet timeout_ms items",
            "add_balloon" => "document sheet timeout_ms items mode symbol prompts layout allow_model_bom_change",
            "export_drawing" => "document timeout_ms format output_path sheets all_sheets dpi overwrite_existing silent",
            _ => "document sheet timeout_ms region_mm width height inline output_path"
        };
        var allowed = fields.Split(' ');
        foreach (var property in p.Properties()) if (!allowed.Contains(property.Name)) throw new ArgumentException("Unknown parameter: " + property.Name);
        foreach (var field in new[] { "references", "visible", "design_view_associative", "hidden_line_all_bodies", "suppressed", "rebuild", "allow_model_bom_change", "all_sheets", "overwrite_existing", "silent", "inline" }) if (Present(p, field) && p[field]!.Type != JTokenType.Boolean) throw new ArgumentException(field + " must be boolean.");
        foreach (var field in new[] { "name", "code", "view", "parent_view" }) if (Present(p, field) && (p[field]!.Type != JTokenType.String || ((string)p[field]!).Length > 128 || ((string)p[field]!).IndexOfAny(new[] { '\r', '\n' }) >= 0)) throw new ArgumentException(field + " must be a single-line name of at most 128 characters.");
        if (p["timeout_ms"] is { Type: not JTokenType.Null }) Range(p, "timeout_ms", 1000, 600000);
        Finite(p);
        foreach (var field in new[] { "annotation_defaults", "prompts", "detail_region_mm", "inherit_3d", "align", "crop", "layout", "region_mm" })
            if (Present(p, field) && p[field] is not JObject) throw new ArgumentException(field + " must be an object.");
        if (Present(p, "iproperties") && p["iproperties"] is not JArray) throw new ArgumentException("iproperties must be an array.");
        switch (command)
        {
            case "get_drawing_info":
                Choice(p, "include", "summary", "items"); Range(p, "max_items", 1, 1000); Range(p, "offset", 0, int.MaxValue); break;
            case "new_drawing":
                Required(p, "name", "template"); Choice(p, "projection", "template", "first_angle", "third_angle"); break;
            case "add_sheet":
                Required(p, "name"); Choice(p, "size", "A0", "A1", "A2", "A3", "A4", "custom"); Choice(p, "orientation", "landscape", "portrait");
                if ((string?)p["size"] == "custom") { Required(p, "width_mm", "height_mm"); Positive(p, "width_mm", "height_mm"); }
                break;
            case "set_title_block":
                Required(p, "document"); Any(p, "prompts", "iproperties", "title_block"); break;
            case "add_drawing_view":
                Required(p, "name", "kind", "position_mm"); Point(p["position_mm"], 2); Positive(p, "scale"); Range(p, "margin_mm", 0, double.MaxValue);
                Choice(p, "kind", "base", "projected", "arbitrary", "detail");
                var kind = (string?)p["kind"];
                if (kind == "base" || kind == "arbitrary") Required(p, "model"); else Required(p, "parent_view");
                if (kind == "arbitrary") { Required(p, "eye_direction"); Point(p["eye_direction"], 3); }
                if (kind == "detail") Required(p, "detail_region_mm");
                else if (Present(p, "detail_region_mm")) throw new ArgumentException("detail_region_mm requires kind=detail.");
                break;
            case "add_section_view":
                Required(p, "name", "parent_view", "cut_line_mm", "position_mm"); Point(p["position_mm"], 2);
                if (p["cut_line_mm"] is not JArray cut || cut.Count != 2) throw new ArgumentException("cut_line_mm needs two points.");
                Point(cut[0], 2); Point(cut[1], 2); if (JToken.DeepEquals(cut[0], cut[1])) throw new ArgumentException("Section cut points must differ.");
                Choice(p, "direction", "positive", "negative"); Positive(p, "scale", "depth_mm"); break;
            case "edit_drawing_view":
                Required(p, "document", "sheet", "view"); Any(p, "position_mm", "align", "scale", "style", "label", "design_view", "suppressed", "rotation_deg", "crop", "reference_display", "margin_mm", "hidden_line_all_bodies");
                if (Present(p, "position_mm")) Point(p["position_mm"], 2); Positive(p, "scale"); Range(p, "margin_mm", 0, double.MaxValue); break;
            case "add_drawing_dimension":
            case "add_balloon":
                if (p["items"] is not JArray items || items.Count < 1 || items.Count > 100) throw new ArgumentException("items must contain 1..100 objects.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                var total = 0;
                foreach (var token in items)
                {
                    if (token is not JObject item) throw new ArgumentException("Every item must be an object.");
                    foreach (var field in new[] { "intent", "target_region_mm" })
                        if (Present(item, field) && item[field] is not JObject) throw new ArgumentException(field + " must be an object.");
                    Required(item, "name", "view"); Positive(item, "tolerance_mm");
                    var name = (string)item["name"]!;
                    if (!names.Add(name)) throw new ArgumentException("Duplicate item name: " + name);
                    if (command == "add_balloon") { Required(item, "occurrence_path", "text"); if (!Present(p, "layout")) { Required(item, "position_mm"); Point(item["position_mm"], 2); } total++; continue; }
                    Required(item, "kind", "intents"); Choice(item, "kind", "horizontal", "vertical", "aligned", "diameter", "radius", "angular", "chain");
                    if (item["intents"] is not JArray intents) throw new ArgumentException("intents must be an array.");
                    var dimKind = (string?)item["kind"];
                    var needed = dimKind == "diameter" || dimKind == "radius" ? 1 : 2;
                    if (dimKind == "chain")
                    {
                        if (intents.Count < 2 || item["text_positions_mm"] is not JArray positions || positions.Count != intents.Count - 1) throw new ArgumentException("chain requires N intents and N-1 text positions.");
                        Choice(item, "direction", "horizontal", "vertical", "aligned");
                        foreach (var point in positions) Point(point, 2);
                        for (var i = 1; i < intents.Count; i++) if (!names.Add(name + ":" + i)) throw new ArgumentException("Generated chain name conflicts.");
                        total += positions.Count;
                    }
                    else { if (intents.Count != needed) throw new ArgumentException(dimKind + " needs " + needed + " intents."); Required(item, "text_position_mm"); Point(item["text_position_mm"], 2); total++; }
                    foreach (var intent in intents) if (intent is not JObject) throw new ArgumentException("Each intent must be an object.");
                    Range(item, "precision", 0, 8);
                }
                if (total > 100) throw new ArgumentException("Flattened batch exceeds 100 items.");
                break;
            case "export_drawing":
                Required(p, "document", "format", "output_path"); Choice(p, "format", "pdf", "autocad_dwg", "native_idw"); Range(p, "dpi", 72, 2400);
                if ((string?)p["format"] != "native_idw" && (p.Value<bool?>("all_sheets") != true) && (p["sheets"] is not JArray sheets || sheets.Count == 0)) throw new ArgumentException("Select sheets explicitly or set all_sheets=true.");
                if (p.Value<bool?>("all_sheets") == true && Present(p, "sheets")) throw new ArgumentException("Use sheets or all_sheets, not both.");
                if ((string?)p["format"] == "native_idw" && Present(p, "sheets")) throw new ArgumentException("native_idw copies the whole document."); break;
            case "capture_sheet":
                Required(p, "document", "sheet"); Range(p, "width", 64, 4096); Range(p, "height", 64, 4096); break;
        }
        if (Present(p, "style")) Choice(p, "style", "hidden_line_removed", "hidden_line", "shaded", "shaded_hidden_line");
    }

    public static bool Present(JObject p, string key) => p[key] is { Type: not JTokenType.Null };
    public static void Required(JObject p, params string[] keys) { foreach (var key in keys) if (!Present(p, key) || (p[key]!.Type == JTokenType.String && string.IsNullOrWhiteSpace((string?)p[key]))) throw new ArgumentException(key + " is required."); }
    public static void Any(JObject p, params string[] keys) { if (!keys.Any(k => Present(p, k))) throw new ArgumentException("Supply at least one change: " + string.Join(", ", keys)); }
    public static void Point(JToken? t, int size) { if (t is not JArray a || a.Count != size || a.Any(x => x.Type != JTokenType.Integer && x.Type != JTokenType.Float)) throw new ArgumentException("Expected " + size + " finite coordinates."); Finite(a); }
    public static void Choice(JObject p, string key, params string[] values) { if (Present(p, key) && !values.Contains((string?)p[key], StringComparer.Ordinal)) throw new ArgumentException(key + " must be one of " + string.Join(", ", values)); }
    public static void Positive(JObject p, params string[] keys) { foreach (var key in keys) if (Present(p, key) && (!Numeric(p[key]!) || p.Value<double>(key) <= 0)) throw new ArgumentException(key + " must be a positive number."); }
    public static void Range(JObject p, string key, double min, double max) { if (Present(p, key) && (!Numeric(p[key]!) || p.Value<double>(key) < min || p.Value<double>(key) > max)) throw new ArgumentException(key + " is outside " + min + ".." + max); }
    private static bool Numeric(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
    private static void Finite(JToken t) { if (t.Type == JTokenType.Float && (double.IsNaN(t.Value<double>()) || double.IsInfinity(t.Value<double>()))) throw new ArgumentException("Numbers must be finite."); if (t is JContainer c) foreach (var child in c.Children()) Finite(child); }
    public static string Signature(JObject p)
    {
        var copy = (JObject)p.DeepClone(); copy.Remove("timeout_ms"); copy.Remove("document"); copy.Remove("sheet");
        return Canonical(copy).ToString(Formatting.None);
    }
    private static JToken Canonical(JToken t) => t is JObject o ? new JObject(o.Properties().OrderBy(x => x.Name, StringComparer.Ordinal).Where(x => x.Value.Type != JTokenType.Null).Select(x => new JProperty(x.Name, Canonical(x.Value)))) : t is JArray a ? new JArray(a.Select(Canonical)) : t.DeepClone();
}
