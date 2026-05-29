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
/// <c>capture_view</c> — read-only. Renders the active view to a bounded PNG via
/// <c>Camera.SaveAsBitmap</c> (writing to a temp file), then returns it base64-encoded. Width/height are
/// clamped both server-side and here; the resulting base64 is size-guarded so the response never blows
/// the transport bound.
/// </summary>
public sealed class CaptureViewHandler : HandlerBase, IInventorCommand
{
    public string Name => "capture_view";
    public bool IsReadOnly => true;

    // Hard cap on the encoded image so a huge render can't overflow the response guard.
    private const int MaxBase64Bytes = 3_500_000;

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

        var tempPng = IoPath.Combine(IoPath.GetTempPath(), "ipt-mcp-capture-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            // SaveAsBitmap honors the file extension (.png) for the encoding. topColor/bottomColor null = current bg.
            view.Camera.SaveAsBitmap(tempPng, width, height, Type.Missing, Type.Missing);

            byte[] bytes = IoFile.ReadAllBytes(tempPng);
            var base64 = Convert.ToBase64String(bytes);
            if (base64.Length > MaxBase64Bytes)
                return Fail(ctx, InventorErrorCodes.RESPONSE_TOO_LARGE,
                    $"captured image is too large ({base64.Length} base64 bytes); request a smaller width/height");

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
}
#endif
