#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using IoPath = System.IO.Path;
using IoFile = System.IO.File;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>capture_view</c> — read-only. Renders the active view via <c>Camera.SaveAsBitmap</c>.
/// File mode is the default (spec F3-c): without <c>output_path</c> the PNG lands under the
/// capture root's <c>captures\</c> dir and only its path/size come back — no base64. The old
/// base64 response is still available via <c>inline=true</c>, bounded to 256 KiB. Width/height
/// are clamped both server-side and here.
/// </summary>
public sealed class CaptureViewHandler : HandlerBase, IInventorCommand
{
    public string Name => "capture_view";
    public bool IsReadOnly => true;

    private static int _captureSeq;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        global::Inventor.Document? doc;
        try { doc = app.ActiveDocument; } catch { doc = null; }
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document to capture");

        View? view = null;
        try { view = app.ActiveView; } catch { /* none */ }
        if (view is null)
            return Fail(ctx, InventorErrorCodes.API_ERROR, "no active view to capture");

        var width = Clamp(p.Value<int?>("width") ?? 1280);
        var height = Clamp(p.Value<int?>("height") ?? 720);

        // E5: view state + framing in the same call (design view, visibility, orientation/camera).
        JToken? viewState = null;
        if (SetViewStateHandler.HasViewStateKeys(p))
        {
            var vs = RunSub(ctx, "set_view_state", new JObject
            {
                ["design_view"] = p["design_view"], ["design_view_create"] = p["design_view_create"],
                ["object_visibility"] = p["object_visibility"], ["occurrence_visibility"] = p["occurrence_visibility"],
            });
            if (!vs.Ok) return vs;
            viewState = vs.Data;
        }

        if (p["shots"] is { Type: not JTokenType.Null } shotsToken)
        {
            if (shotsToken is not JArray shots || shots.Count == 0 || shots.Count > 12)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "shots must be an array of 1..12 {orientation? | camera?, fit?, output_path?}");
            var captures = new JArray();
            for (var i = 0; i < shots.Count; i++)
            {
                if (shots[i] is not JObject shot) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"shots[{i}] must be an object");
                var framed = Frame(ctx, shot);
                if (framed != null) return framed;
                var shotPath = (string?)shot["output_path"];
                var cap = CaptureToFile(ctx, view, shotPath, width, height);
                if (!cap.Ok) return cap;
                var row = (JObject)cap.Data!;
                row["orientation"] = shot["orientation"];
                captures.Add(row);
            }
            var multi = new JObject { ["captures"] = captures, ["count"] = captures.Count };
            if (viewState != null) multi["view_state"] = viewState;
            return Ok(ctx, multi);
        }

        var framing = Frame(ctx, p);
        if (framing != null) return framing;
        var single = CaptureSingle(ctx, p, view, width, height);
        if (single.Ok && viewState != null && single.Data is JObject sd) sd["view_state"] = viewState;
        return single;
    }

    private InventorCommandResult CaptureSingle(InventorCommandContext ctx, JObject p, View view, int width, int height)
    {

        // File mode: when output_path is supplied, write the PNG/JPG/BMP straight to disk and return
        // only the path (no inline base64). Preferred for larger images — avoids the response token cap.
        var outputPath = (p["output_path"]?.Type == JTokenType.String) ? (string)p["output_path"]! : null;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            if (ExportPathPolicy.TryRejectPath(outputPath, out var pathRejection))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, pathRejection);
            if (CaptureImagePolicy.TryRejectImageExtension(outputPath, out var extRejection))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, extRejection);
            try
            {
                view.Camera.SaveAsBitmap(outputPath, width, height, Type.Missing, Type.Missing);
            }
            catch (Exception ex)
            {
                return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to capture view to file: " + ex.Message);
            }
            return Ok(ctx, new JObject
            {
                ["saved"] = true,
                ["output_path"] = outputPath,
                ["format"] = CaptureImagePolicy.ResolveFormat(outputPath),
                ["width"] = width,
                ["height"] = height,
            });
        }

        // Default (spec F3-c): file mode — generate captures\capture-<ts>-<seq>.png under the
        // capture root and return only its path/size. The root is always an allowed export
        // root by construction, so no ExportPathPolicy check is needed here.
        var inline = p.Value<bool?>("inline") == true;
        if (!inline)
        {
            var root = CaptureImagePolicy.ResolveCaptureRoot();
            // Reserve the name atomically (FileMode.CreateNew) — multiple Inventor instances
            // share the captures dir, so a plain File.Exists check could race.
            var capturePath = CaptureImagePolicy.TryReserveCapturePath(
                root, DateTime.UtcNow,
                System.Threading.Interlocked.Increment(ref _captureSeq));
            if (capturePath is null)
                return Fail(ctx, InventorErrorCodes.API_ERROR,
                    "could not allocate a unique capture filename under " + root);
            try
            {
                view.Camera.SaveAsBitmap(capturePath, width, height, Type.Missing, Type.Missing);
            }
            catch (Exception ex)
            {
                return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to capture view to file: " + ex.Message);
            }
            return Ok(ctx, new JObject
            {
                ["path"] = capturePath,
                ["width"] = width,
                ["height"] = height,
                ["bytes"] = new System.IO.FileInfo(capturePath).Length,
            });
        }

        // inline=true: legacy base64 response, bounded to 256 KiB.
        var tempPng = IoPath.Combine(IoPath.GetTempPath(), "ipt-mcp-capture-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            // SaveAsBitmap honors the file extension (.png) for the encoding. topColor/bottomColor null = current bg.
            view.Camera.SaveAsBitmap(tempPng, width, height, Type.Missing, Type.Missing);

            byte[] bytes = IoFile.ReadAllBytes(tempPng);
            var base64 = Convert.ToBase64String(bytes);
            if (CaptureImagePolicy.TryRejectInline(base64.Length, out var inlineRejection))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, inlineRejection);

            return Ok(ctx, new JObject
            {
                ["mime_type"] = "image/png",
                ["width"] = width,
                ["height"] = height,
                ["bytes"] = bytes.Length,
                ["base64"] = base64,
            });
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to capture view: " + ex.Message);
        }
        finally
        {
            try { if (IoFile.Exists(tempPng)) IoFile.Delete(tempPng); } catch { /* best effort */ }
        }
        }

    private static int Clamp(int px) => px < 16 ? 16 : (px > 4096 ? 4096 : px);

    private static InventorCommandResult RunSub(InventorCommandContext ctx, string command, JObject p)
    {
        if (ctx.Commands is null || !ctx.Commands.TryGetValue(command, out var h))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.API_ERROR, command + " is not available",
                new InventorResponseMeta { TargetId = ctx.TargetId });
        return h.Execute(ctx, p);
    }

    /// <summary>orientation → camera → fit, as requested by <paramref name="p"/>. Null on success.</summary>
    private static InventorCommandResult? Frame(InventorCommandContext ctx, JObject p)
    {
        var orientation = (string?)p["orientation"];
        var camera = p["camera"] as JObject;
        var fit = p["fit"]?.Type == JTokenType.Boolean ? (bool)p["fit"]! : (bool?)null;
        if (!string.IsNullOrWhiteSpace(orientation))
        {
            var r = RunSub(ctx, "set_view_orientation", new JObject { ["orientation"] = orientation, ["fit"] = fit ?? true });
            if (!r.Ok) return r;
        }
        if (camera != null)
        {
            var cp = (JObject)camera.DeepClone();
            if (fit is { } f && cp["fit"] is null) cp["fit"] = f;
            var r = RunSub(ctx, "set_camera", cp);
            if (!r.Ok) return r;
        }
        else if (string.IsNullOrWhiteSpace(orientation) && fit == true)
        {
            var r = RunSub(ctx, "view_fit", new JObject());
            if (!r.Ok) return r;
        }
        return null;
    }

    /// <summary>One PNG/JPG/BMP to <paramref name="outputPath"/> (policy-checked) or to an auto-named capture file.</summary>
    private InventorCommandResult CaptureToFile(InventorCommandContext ctx, View view, string? outputPath, int width, int height)
    {
        string path;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            if (ExportPathPolicy.TryRejectPath(outputPath!, out var pathRejection))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, pathRejection);
            if (CaptureImagePolicy.TryRejectImageExtension(outputPath!, out var extRejection))
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, extRejection);
            path = outputPath!;
        }
        else
        {
            var root = CaptureImagePolicy.ResolveCaptureRoot();
            var reserved = CaptureImagePolicy.TryReserveCapturePath(root, DateTime.UtcNow,
                System.Threading.Interlocked.Increment(ref _captureSeq));
            if (reserved is null)
                return Fail(ctx, InventorErrorCodes.API_ERROR, "could not allocate a unique capture filename under " + root);
            path = reserved;
        }
        try
        {
            view.Camera.SaveAsBitmap(path, width, height, Type.Missing, Type.Missing);
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to capture view to file: " + ex.Message);
        }
        return Ok(ctx, new JObject
        {
            ["path"] = path,
            ["width"] = width,
            ["height"] = height,
            ["bytes"] = new System.IO.FileInfo(path).Length,
        });
    }
}
#endif
