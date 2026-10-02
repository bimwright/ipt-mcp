using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

public sealed class DrawingDimensionItem
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("view")] public required string View { get; set; }
    [JsonPropertyName("kind")] public required string Kind { get; set; }
    [JsonPropertyName("intents"), Description("Intent objects: model_point_mm:[x,y,z] or model_edge reference, optional occurrence_path and point_intent=start|end|mid|center. Ambiguous matches fail.")] public required JsonElement[] Intents { get; set; }
    [JsonPropertyName("text_position_mm")] public double[]? TextPosition { get; set; }
    [JsonPropertyName("text_positions_mm")] public double[][]? ChainPositions { get; set; }
    [JsonPropertyName("direction")] public string? Direction { get; set; }
    [JsonPropertyName("style")] public string? Style { get; set; }
    [JsonPropertyName("precision")] public int? Precision { get; set; }
    [JsonPropertyName("text_override")] public string? TextOverride { get; set; }
    [JsonPropertyName("tolerance_mm")] public double Tolerance { get; set; } = 0.5;
}

public sealed class DrawingBalloonItem
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("view")] public required string View { get; set; }
    [JsonPropertyName("occurrence_path")] public required string OccurrencePath { get; set; }
    [JsonPropertyName("text")] public required string Text { get; set; }
    [JsonPropertyName("position_mm")] public double[]? Position { get; set; }
    [JsonPropertyName("intent")] public JsonElement? Intent { get; set; }
    [JsonPropertyName("target_region_mm")] public JsonElement? TargetRegion { get; set; }
    [JsonPropertyName("tolerance_mm")] public double Tolerance { get; set; } = 0.5;
}

public sealed class DrawingTableColumn
{
    [JsonPropertyName("heading")] public required string Heading { get; set; }
    [JsonPropertyName("width_mm")] public required double WidthMm { get; set; }
}

internal static class DrawingWire
{
    internal static async Task<string> Call(PluginClient client, string command, object args, int? timeout, CancellationToken ct)
    {
        try
        {
            var p = JObject.Parse(JsonSerializer.Serialize(args));
            if (timeout.HasValue) p["timeout_ms"] = timeout.Value;
            DrawingInput.Validate(command, p);
            var data = await client.SendAsync(command, p, ct, timeoutMs: timeout);
            return ToolResponse.Serialize(data);
        }
        catch (ArgumentException ex) { return ToolResponse.Error(InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (InventorGatewayException ex) { return ToolResponse.Error(ex.Code, ex.Message); }
    }
}

[McpServerToolType, Toolset("drawing_query")]
public sealed class DrawingQueryTools
{
    private readonly PluginClient _client;
    public DrawingQueryTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_get_drawing_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Inspect an already-loaded drawing without changing it. include=summary returns sheets, views, definitions and annotation counts; items returns bounded annotation locators. max_items (1..1000) and offset apply to each collection. Sheet coordinates and sizes are mm from the lower-left. Omit document/sheet for the active drawing/sheet. Inventor 2027 first; other hosts report UNSUPPORTED_HOST.")]
    public Task<string> GetDrawingInfo(string? document = null, string? sheet = null, string include = "summary", int max_items = 200, int offset = 0, bool references = true, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "get_drawing_info", new { document, sheet, include, max_items, offset, references }, timeout_ms, ct);
}

[McpServerToolType, Toolset("drawing")]
public sealed class DrawingTools
{
    private readonly PluginClient _client;
    public DrawingTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_new_drawing", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Create a named unsaved drawing from an absolute existing Inventor IDW/native DWG template. projection=template|first_angle|third_angle. Repeating identical input returns the existing managed drawing; a conflicting name fails. Does not save. Inventor 2027 first.")]
    public Task<string> NewDrawing(string name, string template, string projection = "template", JsonElement? annotation_defaults = null, bool visible = true, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "new_drawing", new { name, template, projection, annotation_defaults, visible }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_add_sheet", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Add a named drawing sheet with persistent code. size=A0..A4|custom; orientation=landscape|portrait; custom dimensions are mm. Border/title block are exact existing definition names; omission uses an unambiguous template default. Prompts map labels to strings. Identical managed creates are reused; conflicting inputs fail. No save.")]
    public Task<string> AddSheet(string name, string? document = null, string? code = null, string size = "A1", string orientation = "landscape", double? width_mm = null, double? height_mm = null, string? border = null, string? title_block = null, Dictionary<string, string>? prompts = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "add_sheet", new { name, document, code, size, orientation, width_mm, height_mm, border, title_block, prompts }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_set_title_block", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Update a drawing title block/prompts/iProperties in one transaction. Explicit document required. Definition and prompt labels must already exist. iProperties entries are {set?,name,value}; Title resolves to Summary Information. Returns applied values; never saves.")]
    public Task<string> SetTitleBlock(string document, string? sheet = null, string? title_block = null, Dictionary<string, string>? prompts = null, JsonElement? iproperties = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "set_title_block", new { document, sheet, title_block, prompts, iproperties }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_add_drawing_view", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Create a named base/projected/arbitrary/detail view at position_mm. base/arbitrary require an already-loaded model; projected/detail require parent_view. Scale is a ratio. Arbitrary uses finite eye/up vectors; circular detail uses {center:[x,y],radius_mm}. style=hidden_line_removed|hidden_line|shaded|shaded_hidden_line. No save; conflicting names fail.")]
    public Task<string> AddDrawingView(string name, string kind, double[] position_mm, string? document = null, string? sheet = null, string? model = null, string? parent_view = null, string orientation = "front", double scale = 1, string style = "hidden_line_removed", double[]? eye_direction = null, double[]? up_direction = null, JsonElement? detail_region_mm = null, string? design_view = null, bool design_view_associative = true, string? label = null, string? reference_display = null, double? margin_mm = null, bool? hidden_line_all_bodies = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "add_drawing_view", new { name, kind, position_mm, document, sheet, model, parent_view, orientation, scale, style, eye_direction, up_direction, detail_region_mm, design_view, design_view_associative, label, reference_display, margin_mm, hidden_line_all_bodies }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_add_section_view", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Create a named section from two sheet-space cut_line_mm points on parent_view. direction=positive|negative; omit depth_mm for full depth. Optional inherit_3d={name,position_mm} creates a dependent projected section in the same transaction. Coordinates mm, rotation degrees; no save.")]
    public Task<string> AddSectionView(string name, string parent_view, double[][] cut_line_mm, double[] position_mm, string? document = null, string? sheet = null, string direction = "positive", double? depth_mm = null, double? scale = null, string? style = null, double rotation_deg = 0, string? label = null, JsonElement? inherit_3d = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "add_section_view", new { name, parent_view, cut_line_mm, position_mm, document, sheet, direction, depth_mm, scale, style, rotation_deg, label, inherit_3d }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_edit_drawing_view", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Edit an explicitly scoped drawing view. Positions mm, rotation degrees, scale ratio. align={axis:x|y,with_view,offset_mm}; crop={region_mm:{min,max}}. Reject operations that would lose dependent views/annotations. Shaded moves requiring recreation need rebuild=true and verified dependency preservation. No save.")]
    public Task<string> EditDrawingView(string document, string sheet, string view, double[]? position_mm = null, JsonElement? align = null, double? scale = null, string? style = null, string? label = null, string? design_view = null, bool? suppressed = null, double? rotation_deg = null, JsonElement? crop = null, bool rebuild = false, string? reference_display = null, double? margin_mm = null, bool? hidden_line_all_bodies = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "edit_drawing_view", new { document, sheet, view, position_mm, align, scale, style, label, design_view, suppressed, rotation_deg, crop, rebuild, reference_display, margin_mm, hidden_line_all_bodies }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_add_drawing_dimension", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Create 1..100 named attached dimensions atomically. Kinds horizontal/vertical/aligned (2 intents), diameter/radius (1 circular intent), angular (2 line intents), chain (N intents,N-1 text positions,direction). Resolve model geometry uniquely; ambiguity fails before writes. Return measured values separately from text_override. Names conflict safely; no save.")]
    public Task<string> AddDrawingDimension(DrawingDimensionItem[] items, string? document = null, string? sheet = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "add_drawing_dimension", new { items, document, sheet }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_add_balloon", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Create 1..100 named leadered symbol balloons attached to visible occurrence geometry. mode=symbol requires an existing symbol definition and prompt values. layout={column_x_mm,start_y_mm,spacing_mm,leader_angle_deg}. Native mode=bom requires allow_model_bom_change=true and cross-document rollback support; fails if unverified. Never saves models.")]
    public Task<string> AddBalloon(DrawingBalloonItem[] items, string? document = null, string? sheet = null, string mode = "symbol", string? symbol = null, Dictionary<string, string>? prompts = null, JsonElement? layout = null, bool allow_model_bom_change = false, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "add_balloon", new { items, document, sheet, mode, symbol, prompts, layout, allow_model_bom_change }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_export_drawing", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Export an explicitly scoped drawing to PDF, per-sheet AutoCAD DWG, or whole native IDW copy. output_path must be absolute and allowlisted. Select sheets or all_sheets=true for translators. Existing files require overwrite_existing=true. Per-file statuses and partial failure are explicit. Native copy never renames the source; no model save.")]
    public Task<string> ExportDrawing(string document, string format, string output_path, string[]? sheets = null, bool all_sheets = false, int dpi = 300, bool overwrite_existing = false, bool silent = true, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "export_drawing", new { document, format, output_path, sheets, all_sheets, dpi, overwrite_existing, silent }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_capture_sheet", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Write a PNG capture of one explicitly scoped drawing sheet or region_mm={min:[x,y],max:[x,y]}. Defaults 1600x1100. Restores active document/sheet/camera. Path omitted uses capture root; explicit paths never overwrite. inline=true adds base64 only within 256 KiB; returned file remains available. Writes files, so read-only mode rejects this tool.")]
    public Task<string> CaptureSheet(string document, string sheet, JsonElement? region_mm = null, int width = 1600, int height = 1100, bool inline = false, string? output_path = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "capture_sheet", new { document, sheet, region_mm, width, height, inline, output_path }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_add_drawing_note", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Create a named note at position_mm (sheet mm). kind=general|sheet_title creates fitted text; kind=leader requires view and intent={model_edge or model_point_mm:[x,y,z],point_intent:start|end|mid|center,occurrence_path?}, resolved uniquely. Text is literal. style is an exact existing text style for fitted notes or dimension style for leader notes; layer is an exact existing layer. Optional box_mm={width,height} fixes a fitted note box in mm. Repeats reuse identical managed notes; changed content/placement conflicts. Returns actual text/style/layer/attachment; no save.")]
    public Task<string> AddDrawingNote(string name, string text, double[] position_mm, string? document = null, string? sheet = null, string kind = "general", string? style = null, string? layer = null, string? view = null, JsonElement? intent = null, JsonElement? box_mm = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "add_drawing_note", new { name, text, position_mm, document, sheet, kind, style, layer, view, intent, box_mm }, timeout_ms, ct);

    [McpServerTool(Name = "inventor_add_drawing_table", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Create a named custom table. columns=1..50 {heading,width_mm}; rows=0..500 arrays of literal strings matching column count. position_mm is the top-left insertion point; anchor=top_left only. Optional row_heights_mm has one positive height per data row. Data row/column indices are 1-based, excluding title/header. style is an exact existing table style; returns actual title/cells/dimensions/box. BOM parts lists are unavailable. Identical managed creates are reused; conflicting names/content fail. No save.")]
    public Task<string> AddDrawingTable(string name, DrawingTableColumn[] columns, string[][] rows, double[] position_mm, string? document = null, string? sheet = null, string? title = null, string? style = null, string anchor = "top_left", double[]? row_heights_mm = null, int? timeout_ms = null, CancellationToken ct = default)
        => DrawingWire.Call(_client, "add_drawing_table", new { name, columns, rows, position_mm, document, sheet, title, style, anchor, row_heights_mm }, timeout_ms, ct);
}
