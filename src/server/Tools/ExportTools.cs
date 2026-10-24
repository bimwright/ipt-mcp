using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// View capture + export tools (toolset <c>export</c>). These write output files but do not mutate the
/// active Inventor document. Phase 1 has no first-class output-path policy, so <c>export</c> is in
/// <see cref="ToolsetFilter.WriteCapable"/> (hidden under <c>--read-only</c>); the wrappers still apply
/// a basic allowed-output-path check before round-tripping to the add-in. <c>capture_view</c> writes a
/// PNG to the captures dir by default; <c>inline=true</c> returns a bounded base64 PNG.
/// <c>export_dxf</c> must declare its DXF source (sketch or sheet fabrication flat pattern)
/// because Phase 1 ships no drawing tools.
/// </summary>
[McpServerToolType]
[Toolset("export")]
public sealed class ExportTools
{
    private readonly PluginClient _client;
    public ExportTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_capture_view", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Capture the active Inventor view as an image - optionally setting up the view in the same call. Default: writes a PNG to <export-root>\\captures\\ (or output_path when given - absolute, under an allowed root, ending .png/.jpg/.jpeg/.bmp; output_path wins over inline) and returns only {path,width,height,bytes}. Pass inline=true for the legacy base64 response (rejected above 256 KiB). Optional width/height in pixels (clamped). " +
                 "Set-up keys (applied in this order before capturing): design_view (+ design_view_create), object_visibility, occurrence_visibility (same as inventor_set_view_state), then orientation (iso_top_right|front|top|...) or camera {eye,target,up,perspective,extents_mm} (same as inventor_set_camera), then fit. " +
                 "shots=[{orientation? | camera?, fit?, output_path?}] (max 12) captures several views in one call and returns captures[]. Only the view-state keys change the document (design view / visibility); orientation and camera do not.")]
    public Task<string> CaptureView(int width = 1280, int height = 720, string? outputPath = null, bool inline = false,
        string? design_view = null, bool design_view_create = false, [Description("JSON object of booleans, e.g. {all_work_features:false, sketches:false}.")] System.Text.Json.JsonElement? object_visibility = null,
        [Description("JSON array of {selector, visible} rules (or one such object).")] System.Text.Json.JsonElement? occurrence_visibility = null, string? orientation = null, [Description("JSON object {eye, target, up?, perspective?, extents_mm?} (mm).")] System.Text.Json.JsonElement? camera = null,
        bool? fit = null, [Description("JSON array of {orientation? | camera?, fit?, output_path?} objects (max 12).")] System.Text.Json.JsonElement? shots = null, CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["width"] = ClampPixels(width),
            ["height"] = ClampPixels(height),
        };
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
                return Task.FromResult(Error("INVALID_ARGUMENT", rejection));
            p["output_path"] = outputPath;
        }
        if (inline) p["inline"] = true;
        if (!string.IsNullOrWhiteSpace(design_view)) p["design_view"] = design_view;
        if (design_view_create) p["design_view_create"] = true;
        if (JsonArg.From(object_visibility) is { } ov) p["object_visibility"] = ov;
        if (JsonArg.From(occurrence_visibility) is { } occ) p["occurrence_visibility"] = occ;
        if (!string.IsNullOrWhiteSpace(orientation)) p["orientation"] = orientation;
        if (JsonArg.From(camera) is { } cam) p["camera"] = cam;
        if (fit is { } f) p["fit"] = f;
        if (JsonArg.From(shots) is { } sh)
        {
            if (sh is JArray arr)
                foreach (var shot in arr)
                    if (shot?["output_path"] is { Type: JTokenType.String } op && ExportPathPolicy.TryRejectPath((string)op!, out var shotRejection))
                        return Task.FromResult(Error("INVALID_ARGUMENT", shotRejection));
            p["shots"] = sh;
        }
        return Call("capture_view", p, ct);
    }

    [McpServerTool(Name = "inventor_set_view_state", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Set the active document's display state in one call: design_view = design view representation name to activate (design_view_create=true creates it when missing); object_visibility = {all_work_features, origin_work_planes, origin_work_axes, origin_work_points, user_work_planes, user_work_axes, user_work_points, sketches, sketches_3d, sketch_dimensions, ucs_triads, annotations_3d, welds: bool}; occurrence_visibility = [{selector, visible}] (assembly; any depth). Changes display only (the design view / visibility is stored with the document). capture_view accepts the same keys. " + JsonArg.SelectorDoc)]
    public Task<string> SetViewState(string? design_view = null, bool design_view_create = false,
        [Description("JSON object of booleans, e.g. {all_work_features:false, sketches:false}.")] System.Text.Json.JsonElement? object_visibility = null, [Description("JSON array of {selector, visible} rules (or one such object).")] System.Text.Json.JsonElement? occurrence_visibility = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (!string.IsNullOrWhiteSpace(design_view)) p["design_view"] = design_view;
        if (design_view_create) p["design_view_create"] = true;
        if (JsonArg.From(object_visibility) is { } ov) p["object_visibility"] = ov;
        if (JsonArg.From(occurrence_visibility) is { } occ) p["occurrence_visibility"] = occ;
        return Call("set_view_state", p, ct);
    }

    [McpServerTool(Name = "inventor_export_step", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Export the active part or assembly to a STEP (.stp/.step) file at output_path. The path must be an absolute file path under an allowed output root.")]
    public Task<string> ExportStep(string outputPath, CancellationToken ct = default)
    {
        if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection));
        return Call("export_step", new JObject { ["output_path"] = outputPath }, ct);
    }

    [McpServerTool(Name = "inventor_export_stl", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Export the active part or assembly to an STL (.stl) file at output_path. The path must be an absolute file path under an allowed output root.")]
    public Task<string> ExportStl(string outputPath, CancellationToken ct = default)
    {
        if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection));
        return Call("export_stl", new JObject { ["output_path"] = outputPath }, ct);
    }

    [McpServerTool(Name = "inventor_export_sat", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Export the active part or assembly to an ACIS SAT (.sat) file at output_path — the format Revit consumes for geometry interop. acis_version defaults to 7 (ACIS 7.0); the Inventor SAT translator supports ACIS 7 only, so other values are rejected. The path must be an absolute .sat file path under an allowed output root (user profile, temp, or BIMWRIGHT_INVENTOR_EXPORT_ROOT).")]
    public Task<string> ExportSat(string outputPath, double acis_version = 7.0, CancellationToken ct = default)
    {
        if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection));
        if (acis_version != 7.0)
            return Task.FromResult(Error("INVALID_ARGUMENT",
                "acis_version must be 7 — the Inventor SAT translator's Version option supports ACIS 7.0 only."));
        return Call("export_sat", new JObject
        {
            ["output_path"] = outputPath,
            ["acis_version"] = acis_version,
        }, ct);
    }

    [McpServerTool(Name = "inventor_export_bodies_sat", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can overwrite files when overwrite=true; documents are not modified. Export solid bodies one file per body to ACIS SAT for Revit interop, across many part documents in one call: items=[{document, bodies?, key?}] (max 200 items / 2000 bodies). document: full path or name of a part already open or loaded in Inventor (e.g. referenced by the open assembly; never opened); omit for the active part. bodies: 1-based index, 'body:N' or names (default all solids). key: file-name prefix (default the part file name). Files are <out_dir>\\<key>-bNNN.sat (NNN = body index); out_dir must be under an allowed output root (user profile, temp or BIMWRIGHT_INVENTOR_EXPORT_ROOT). All documents, bodies and paths are checked before the first write; existing files are refused unless overwrite=true. verify=true (default) reads each file back and compares body count, face count, volume and bbox (tolerances returned). A body that fails is reported and the rest continue — retry only the failed bodies. Returns a compact summary: requested/exported/verified/failed, max_volume_rel_error, max_bbox_error_mm and failures[{document, key, id, name, file, volume_mm3, bbox_mm, face_count, verify?, error?}]; detail=true also returns that row for every body. Use instead of send_code loops over DataIO.WriteDataToFile.")]
    public Task<string> ExportBodiesSat(
        [Description("JSON array of {document?, bodies?, key?} objects.")] System.Text.Json.JsonElement items,
        string out_dir, bool verify = true, bool overwrite = false, bool detail = false, CancellationToken ct = default)
    {
        if (JsonArg.From(items) is not JArray list || list.Count == 0)
            return Task.FromResult(Error("INVALID_ARGUMENT", "items must be a non-empty array of {document, bodies?, key?}"));
        if (string.IsNullOrWhiteSpace(out_dir))
            return Task.FromResult(Error("INVALID_ARGUMENT", "out_dir is required"));
        if (ExportPathPolicy.TryRejectPath(System.IO.Path.Combine(out_dir, "probe.sat"), out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection.Replace("output_path", "out_dir")));
        return Call("export_bodies_sat", new JObject
        {
            ["items"] = list,
            ["out_dir"] = out_dir,
            ["verify"] = verify,
            ["overwrite"] = overwrite,
            ["detail"] = detail,
        }, ct, timeoutMs: 300_000);
    }

    [McpServerTool(Name = "inventor_derive_envelope", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Create a new part document containing a derived-component feature from a source part/assembly — the envelope path for interop (SAT→Revit) and simplified reference geometry. output_path: absolute .ipt under an allowed output root (the derived document is saved there). source_path defaults to the active document (must have been saved); .ipt sources derive per-solid, .iam sources derive per-occurrence. derive_style=multiple|single_seams|single_no_seams. include_bodies: source solid names or 'body:N' indices to keep (part sources only — incompatible with bounding_box and rejected for .iam). bounding_box=true derives every solid/occurrence as its bounding box — the lightweight-envelope mode. include_parameters carries source parameters. activate=false creates the document hidden. Returns document/path/style/solids-or-occurrences counts.")]
    public Task<string> DeriveEnvelope(
        string outputPath,
        string? sourcePath = null,
        string deriveStyle = "multiple",
        string[]? includeBodies = null,
        bool boundingBox = false,
        bool includeParameters = false,
        bool useOrientedMinBoundingBox = false,
        bool activate = true,
        CancellationToken ct = default)
    {
        if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection));
        if (!outputPath.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Error("INVALID_ARGUMENT", "output_path must end in .ipt"));
        var p = new JObject
        {
            ["output_path"] = outputPath,
            ["derive_style"] = deriveStyle,
            ["bounding_box"] = boundingBox,
            ["include_parameters"] = includeParameters,
            ["use_oriented_min_bounding_box"] = useOrientedMinBoundingBox,
            ["activate"] = activate,
        };
        if (!string.IsNullOrWhiteSpace(sourcePath)) p["source_path"] = sourcePath;
        if (includeBodies is { Length: > 0 }) p["include_bodies"] = new JArray(includeBodies);
        return Call("derive_envelope", p, ct);
    }

    [McpServerTool(Name = "inventor_export_dxf", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Can discard or overwrite persisted data; Inventor undo cannot restore discarded edits or overwritten files. Export a 2D DXF (.dxf) at output_path. Because Phase 1 ships no drawing tools, you MUST declare the DXF source: source=sketch with sketch_name, or source=flat_pattern for a sheet fabrication part. If the source is unavailable on the active document the add-in returns WRONG_DOCUMENT_TYPE or INVALID_ARGUMENT.")]
    public Task<string> ExportDxf(
        string outputPath,
        [Description("DXF source: 'sketch' (requires sketch_name) or 'flat_pattern' (sheet fabrication part).")] string source,
        [Description("Sketch name when source=sketch. Ignored for flat_pattern.")] string? sketchName = null,
        CancellationToken ct = default)
    {
        if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection));

        var normalized = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized != "sketch" && normalized != "flat_pattern")
        {
            return Task.FromResult(Error("INVALID_ARGUMENT",
                "source must be 'sketch' or 'flat_pattern' (Phase 1 has no drawing tools)."));
        }
        if (normalized == "sketch" && string.IsNullOrWhiteSpace(sketchName))
        {
            return Task.FromResult(Error("INVALID_ARGUMENT", "sketch_name is required when source=sketch."));
        }

        return Call("export_dxf", new JObject
        {
            ["output_path"] = outputPath,
            ["source"] = normalized,
            ["sketch_name"] = sketchName,
        }, ct);
    }

    [McpServerTool(Name = "inventor_view_fit", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Zoom-fit the active Inventor view to the model extents. Run before capture_view so captures are never blank. Does not modify the document.")]
    public Task<string> ViewFit(CancellationToken ct = default)
        => Call("view_fit", new JObject(), ct);

    [McpServerTool(Name = "inventor_set_view_orientation", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Set the active view camera to a standard orientation: iso_top_right|iso_top_left|iso_bottom_right|iso_bottom_left|front|back|top|bottom|left|right (fit=true refits). Loop over several orientations + capture_view to photograph a model from multiple angles. Does not modify the document.")]
    public Task<string> SetViewOrientation(string orientation, bool fit = true, CancellationToken ct = default)
        => Call("set_view_orientation", new JObject { ["orientation"] = orientation, ["fit"] = fit }, ct);

    [McpServerTool(Name = "inventor_set_camera", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Position the active view's camera explicitly — use before capture_view when the 10 standard orientations don't fit. eye/target take {x,y,z} or [x,y,z] in mm; up is a direction vector; perspective toggles projection; extents_mm [width,height] sets the view volume; fit=true reframes to model extents (default false — explicit framing is kept). At least one parameter is required; degenerate views (eye==target, zero/parallel up) are rejected. Returns the resolved camera state. Does not modify the document.")]
    public Task<string> SetCamera(
        System.Text.Json.JsonElement? eye = null,
        System.Text.Json.JsonElement? target = null,
        System.Text.Json.JsonElement? up = null,
        bool? perspective = null,
        double[]? extents_mm = null,
        bool fit = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (eye is { } e) p["eye"] = JToken.Parse(e.GetRawText());
        if (target is { } t) p["target"] = JToken.Parse(t.GetRawText());
        if (up is { } u) p["up"] = JToken.Parse(u.GetRawText());
        if (perspective.HasValue) p["perspective"] = perspective.Value;
        if (extents_mm is not null) p["extents_mm"] = new JArray(extents_mm);
        if (fit) p["fit"] = true;
        return Call("set_camera", p, ct);
    }

    // ---- helpers ----

    private static int ClampPixels(int px) => px < 16 ? 16 : (px > 4096 ? 4096 : px);

    private static string Error(string code, string message)
        => ToolResponse.Error(code, message);

    private async Task<string> Call(string command, JObject p, CancellationToken ct, int? timeoutMs = null)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct, timeoutMs);
            return ToolResponse.Serialize(data);
        }
        catch (InventorGatewayException ex)
        {
            return ToolResponse.Error(ex.Code, ex.Message);
        }
    }
}
