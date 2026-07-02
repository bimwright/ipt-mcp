using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>
/// View capture + export tools (toolset <c>export</c>). These write output files but do not mutate the
/// active Inventor document. Phase 1 has no first-class output-path policy, so <c>export</c> is in
/// <see cref="ToolsetFilter.WriteCapable"/> (hidden under <c>--read-only</c>); the wrappers still apply
/// a basic allowed-output-path check before round-tripping to the add-in. <c>capture_view</c> returns a
/// bounded base64 PNG. <c>export_dxf</c> must declare its DXF source (sketch or sheet-metal flat pattern)
/// because Phase 1 ships no drawing tools.
/// </summary>
[McpServerToolType]
public sealed class ExportTools
{
    private readonly PluginClient _client;
    public ExportTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "inventor_capture_view"),
     Description("Capture the active Inventor view as an image. Default: returns a base64-encoded PNG inline (bounded resolution). If output_path is given (absolute path under an allowed root, ending in .png/.jpg/.jpeg/.bmp), the image is written to that file and only the path is returned (no base64) — preferred for larger images to avoid the response size limit. Optional width/height in pixels (clamped). Does not mutate the document.")]
    public Task<string> CaptureView(int width = 1280, int height = 720, string? outputPath = null, CancellationToken ct = default)
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
        return Call("capture_view", p, ct);
    }

    [McpServerTool(Name = "inventor_export_step"),
     Description("Export the active part or assembly to a STEP (.stp/.step) file at output_path. The path must be an absolute file path under an allowed output root.")]
    public Task<string> ExportStep(string outputPath, CancellationToken ct = default)
    {
        if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection));
        return Call("export_step", new JObject { ["output_path"] = outputPath }, ct);
    }

    [McpServerTool(Name = "inventor_export_stl"),
     Description("Export the active part or assembly to an STL (.stl) file at output_path. The path must be an absolute file path under an allowed output root.")]
    public Task<string> ExportStl(string outputPath, CancellationToken ct = default)
    {
        if (ExportPathPolicy.TryRejectPath(outputPath, out var rejection))
            return Task.FromResult(Error("INVALID_ARGUMENT", rejection));
        return Call("export_stl", new JObject { ["output_path"] = outputPath }, ct);
    }

    [McpServerTool(Name = "inventor_export_dxf"),
     Description("Export a 2D DXF (.dxf) at output_path. Because Phase 1 ships no drawing tools, you MUST declare the DXF source: source=sketch with sketch_name, or source=flat_pattern for a sheet-metal part. If the source is unavailable on the active document the add-in returns WRONG_DOCUMENT_TYPE or INVALID_ARGUMENT.")]
    public Task<string> ExportDxf(
        string outputPath,
        [Description("DXF source: 'sketch' (requires sketch_name) or 'flat_pattern' (sheet-metal part).")] string source,
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

    [McpServerTool(Name = "inventor_view_fit"),
     Description("Zoom-fit the active Inventor view to the model extents. Run before capture_view so captures are never blank. Does not modify the document.")]
    public Task<string> ViewFit(CancellationToken ct = default)
        => Call("view_fit", new JObject(), ct);

    [McpServerTool(Name = "inventor_set_view_orientation"),
     Description("Set the active view camera to a standard orientation: iso_top_right|iso_top_left|iso_bottom_right|iso_bottom_left|front|back|top|bottom|left|right (fit=true refits). Loop over several orientations + capture_view (output_path mode) to photograph a model from multiple angles. Does not modify the document.")]
    public Task<string> SetViewOrientation(string orientation, bool fit = true, CancellationToken ct = default)
        => Call("set_view_orientation", new JObject { ["orientation"] = orientation, ["fit"] = fit }, ct);

    // ---- helpers ----

    private static int ClampPixels(int px) => px < 16 ? 16 : (px > 4096 ? 4096 : px);

    private static string Error(string code, string message)
        => JsonConvert.SerializeObject(new { ok = false, error = new { code, message } }, Formatting.Indented);

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        catch (InventorGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.Indented);
        }
    }
}
