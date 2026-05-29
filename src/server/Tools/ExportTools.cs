using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Tools;

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
     Description("Capture the active view as a base64-encoded PNG image (bounded resolution). Optional width/height in pixels (clamped). Does not write a file or mutate the document.")]
    public Task<string> CaptureView(int width = 1280, int height = 720, CancellationToken ct = default)
        => Call("capture_view", new JObject
        {
            ["width"] = ClampPixels(width),
            ["height"] = ClampPixels(height),
        }, ct);

    [McpServerTool(Name = "inventor_export_step"),
     Description("Export the active part or assembly to a STEP (.stp/.step) file at output_path. The path must be an absolute file path under an allowed output root.")]
    public Task<string> ExportStep(string outputPath, CancellationToken ct = default)
    {
        if (TryRejectPath(outputPath, out var rejection)) return Task.FromResult(rejection);
        return Call("export_step", new JObject { ["output_path"] = outputPath }, ct);
    }

    [McpServerTool(Name = "inventor_export_stl"),
     Description("Export the active part or assembly to an STL (.stl) file at output_path. The path must be an absolute file path under an allowed output root.")]
    public Task<string> ExportStl(string outputPath, CancellationToken ct = default)
    {
        if (TryRejectPath(outputPath, out var rejection)) return Task.FromResult(rejection);
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
        if (TryRejectPath(outputPath, out var rejection)) return Task.FromResult(rejection);

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

    // ---- helpers ----

    private static int ClampPixels(int px) => px < 16 ? 16 : (px > 4096 ? 4096 : px);

    /// <summary>
    /// Basic allowed-output-path check (Phase 1 has no full path policy). The path must be an absolute,
    /// rooted file path with a file name. Returns true (with a populated <paramref name="rejection"/> JSON
    /// payload) when the path is rejected.
    /// </summary>
    private static bool TryRejectPath(string outputPath, out string rejection)
    {
        rejection = "";
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            rejection = Error("INVALID_ARGUMENT", "output_path is required.");
            return true;
        }

        string full;
        try
        {
            full = Path.GetFullPath(outputPath);
        }
        catch (Exception ex)
        {
            rejection = Error("INVALID_ARGUMENT", "output_path is not a valid path: " + ex.Message);
            return true;
        }

        if (!Path.IsPathRooted(full) || string.IsNullOrEmpty(Path.GetFileName(full)))
        {
            rejection = Error("INVALID_ARGUMENT", "output_path must be an absolute file path.");
            return true;
        }

        var dir = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(dir) || !IsUnderAllowedRoot(full))
        {
            rejection = Error("INVALID_ARGUMENT",
                "output_path must be under an allowed output root (user profile or temp directory).");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Phase-1 allowed output roots: the user profile tree and the temp directory. This is a basic
    /// guard, not a full policy — it stops obviously out-of-bounds writes (system dirs, drive roots).
    /// </summary>
    private static bool IsUnderAllowedRoot(string fullPath)
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     Path.GetTempPath(),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

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
