#if INVENTOR2027
using System;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Threading;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Export;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using IoPath = System.IO.Path;

namespace Bimwright.Ipt.Shared.Handlers.Drawing;

internal static partial class DrawingOperations
{
    private const string PdfTranslator = "{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}";
    private const string DwgTranslator = "{C24E3AC2-122E-11D5-8E91-0010B541CD80}";
    private static int _captureSequence;
    private static string OutputPath(string path, string extension)
    {
        if (!IoPath.IsPathFullyQualified(path)) throw new ArgumentException("output_path must be fully qualified.");
        if (ExportPathPolicy.TryRejectPath(path, out var error)) throw new ArgumentException(error);
        if (!string.Equals(IoPath.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("output_path must end in " + extension);
        return IoPath.GetFullPath(path);
    }
    private static string SheetFileCode(Sheet s)
    {
        var code = DrawingSupport.Read(s.AttributeSets, "code") ?? s.Name;
        var invalid = IoPath.GetInvalidFileNameChars(); var safe = new string(code.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        if (safe.Length == 0) throw new ArgumentException("Sheet code/name does not produce a valid filename."); return safe;
    }
    private static DrawingDocument CopySheets(Application app, DrawingDocument source, Sheet[] sheets)
    {
        var copy = (DrawingDocument)app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject, app.FileManager.GetTemplateFile(DocumentTypeEnum.kDrawingDocumentObject), false);
        try
        {
            var initial = copy.Sheets.Cast<Sheet>().ToArray();
            foreach (var s in sheets) s.CopyTo((Inventor._DrawingDocument)(object)copy);
            foreach (var s in initial) s.Delete(); copy.Update(); return copy;
        }
        catch { copy.Close(true); throw; }
    }
    private static InventorCommandResult Export(InventorCommandContext ctx, DrawingDocument d, JObject p)
    {
        var app = DrawingSupport.App(ctx); var format = (string)p["format"]!; var extension = format == "pdf" ? ".pdf" : format == "native_idw" ? ".idw" : ".dwg";
        var output = OutputPath((string)p["output_path"]!, extension); var overwrite = p.Value<bool?>("overwrite_existing") ?? false;
        var requested = p["sheets"] as JArray;
        var sheets = requested == null ? d.Sheets.Cast<Sheet>().ToArray() : requested.Select(x => DrawingSupport.Sheet(d, new JObject { ["sheet"] = x })).ToArray();
        if (sheets.Length == 0 || sheets.Select(x => x.InternalName).Distinct().Count() != sheets.Length) throw new ArgumentException("Select nonempty, unique sheets.");
        if (format == "native_idw" && d.IsInventorDWG) throw new ArgumentException("native_idw requires an IDW source; format conversion is not a native copy.");
        var jobs = format == "autocad_dwg" ? sheets.Select(s => (path: IoPath.Combine(IoPath.GetDirectoryName(output)!, IoPath.GetFileNameWithoutExtension(output) + "-" + SheetFileCode(s) + ".dwg"), sheets: new[] { s })).ToArray() : new[] { (path: output, sheets) };
        if (jobs.Select(x => x.path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != jobs.Length) throw new ArgumentException("Sheet codes produce duplicate output filenames.");
        foreach (var job in jobs)
        {
            OutputPath(job.path, extension);
            if (string.Equals(job.path, d.FullFileName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Export must not replace the working document.");
            if (File.Exists(job.path) && !overwrite) throw new ArgumentException("Output exists; explicit overwrite_existing=true required: " + job.path);
        }
        var rows = new JArray(); var failed = 0; var dirty = d.Dirty; var sourcePath = d.FullFileName;
        using (SilentOperationScope.Enter(app, p.Value<bool?>("silent") ?? true))
            foreach (var job in jobs)
            {
                var temp = IoPath.Combine(IoPath.GetDirectoryName(job.path)!, ".ipt-mcp-" + Guid.NewGuid().ToString("N") + extension); DrawingDocument? copy = null;
                try
                {
                    Directory.CreateDirectory(IoPath.GetDirectoryName(job.path)!);
                    if (format == "native_idw")
                    {
                        var saveOptions = app.TransientObjects.CreateNameValueMap();
                        saveOptions.Add("SaveDependents", false);
                        d.SaveAs2(temp, true, saveOptions);
                    }
                    else
                    {
                        // A hidden disposable copy supports ordered/noncontiguous subsets without
                        // changing ExcludeFromPrinting or activating sheets in the source document.
                        copy = CopySheets(app, d, job.sheets);
                        var translator = ExportSupport.GetTranslator(app, format == "pdf" ? PdfTranslator : DwgTranslator, format);
                        ExportSupport.SaveCopyAs(app, translator, copy, temp, options =>
                        {
                            if (format == "pdf") { options.Value["Sheet_Range"] = PrintRangeEnum.kPrintAllSheets; options.Value["Vector_Resolution"] = p.Value<int?>("dpi") ?? 300; }
                        });
                    }
                    var bytes = new FileInfo(temp).Length; if (bytes <= 0) throw new InvalidOperationException("Export produced an empty file.");
                    File.Move(temp, job.path, overwrite);
                    rows.Add(new JObject { ["ok"] = true, ["status"] = "completed", ["path"] = job.path, ["bytes"] = bytes, ["sheets"] = new JArray(job.sheets.Select(x => x.Name)) });
                }
                catch (Exception ex) { failed++; rows.Add(new JObject { ["ok"] = false, ["status"] = "failed", ["path"] = job.path, ["sheets"] = new JArray(job.sheets.Select(x => x.Name)), ["error"] = ex.Message }); }
                finally { if (copy != null) copy.Close(true); if (File.Exists(temp)) File.Delete(temp); }
            }
        var unchanged = d.Dirty == dirty && d.FullFileName == sourcePath;
        return DrawingSupport.Success(ctx, new JObject { ["ok"] = failed == 0 && unchanged, ["completed"] = failed == 0 && unchanged, ["count"] = rows.Count, ["completed_count"] = rows.Count - failed, ["failed_count"] = failed, ["format"] = format, ["files"] = rows, ["source_unchanged"] = unchanged, ["error"] = unchanged ? null : "Export changed source state; inspect before further writes." });
    }
    private static InventorCommandResult Capture(InventorCommandContext ctx, DrawingDocument d, Sheet sheet, JObject p)
    {
        var app = DrawingSupport.App(ctx);
        if (d.Views.Count == 0) throw new NotSupportedException("Capturing a hidden-loaded drawing without changing window visibility is not supported; open its window explicitly first.");
        var width = p.Value<int?>("width") ?? 1600; var height = p.Value<int?>("height") ?? 1100;
        var region = p["region_mm"] as JObject;
        if (region != null) { DrawingInput.Required(region, "min", "max"); DrawingInput.Point(region["min"], 2); DrawingInput.Point(region["max"], 2); if ((double)region["max"]![0]! <= (double)region["min"]![0]! || (double)region["max"]![1]! <= (double)region["min"]![1]!) throw new ArgumentException("Capture region must have positive width/height."); }
        string path;
        if (DrawingInput.Present(p, "output_path"))
        {
            path = OutputPath((string)p["output_path"]!, ".png"); Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
            using (var reservation = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        }
        else path = CaptureImagePolicy.TryReserveCapturePath(CaptureImagePolicy.ResolveCaptureRoot(), DateTime.UtcNow, Interlocked.Increment(ref _captureSequence)) ?? throw new IOException("Unable to reserve capture filename.");
        var original = app.ActiveDocument; var originalSheet = original is DrawingDocument drawing ? drawing.ActiveSheet : null; var selectedSheet = d.ActiveSheet; var dirty = d.Dirty;
        var originalView = app.ActiveView; var originalCamera = originalView.Camera; var targetCamera = d.Views[1].Camera;
        // Camera getters return a detached camera snapshot. Apply only restores view state.
        var savedEye = targetCamera.Eye.Copy(); var savedTarget = targetCamera.Target.Copy(); var savedUp = targetCamera.UpVector.Copy(); targetCamera.GetExtents(out var savedWidth, out var savedHeight); var perspective = targetCamera.Perspective;
        string? restorationError = null; JObject? result = null; Exception? failure = null;
        try
        {
            d.Activate(); sheet.Activate(); var camera = app.ActiveView.Camera; camera.Perspective = false;
            var min = region?["min"] ?? new JArray(0, 0); var max = region?["max"] ?? new JArray(sheet.Width * 10, sheet.Height * 10);
            var cx = ((double)min[0]! + (double)max[0]!) / 20; var cy = ((double)min[1]! + (double)max[1]!) / 20;
            var tg = app.TransientGeometry; camera.Target = tg.CreatePoint(cx, cy, 0); camera.Eye = tg.CreatePoint(cx, cy, 100); camera.UpVector = tg.CreateUnitVector(0, 1, 0);
            var extentW = ((double)max[0]! - (double)min[0]!) / 10; var extentH = ((double)max[1]! - (double)min[1]!) / 10;
            if (extentW / extentH < (double)width / height) extentW = extentH * width / height; else extentH = extentW * height / width;
            camera.SetExtents(extentW, extentH); camera.ApplyWithoutTransition(); camera.SaveAsBitmap(path, width, height);
            var bytes = new FileInfo(path).Length; if (bytes == 0) throw new IOException("Capture produced an empty PNG.");
            result = new JObject { ["ok"] = true, ["path"] = path, ["width"] = width, ["height"] = height, ["bytes"] = bytes, ["region_mm"] = new JObject { ["min"] = min.DeepClone(), ["max"] = max.DeepClone() }, ["count"] = 1 };
            if (p.Value<bool?>("inline") == true) { var base64 = Convert.ToBase64String(File.ReadAllBytes(path)); if (CaptureImagePolicy.TryRejectInline(base64.Length, out var reason)) { result["inline_omitted"] = true; result["warning"] = reason; } else { result["mime_type"] = "image/png"; result["base64"] = base64; } }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            try
            {
                selectedSheet.Activate(); var camera = d.Views[1].Camera; camera.Eye = savedEye; camera.Target = savedTarget; camera.UpVector = savedUp; camera.Perspective = perspective; camera.SetExtents(savedWidth, savedHeight); camera.ApplyWithoutTransition();
                original.Activate(); originalSheet?.Activate(); originalCamera.ApplyWithoutTransition();
            }
            catch (Exception ex) { restorationError = ex.Message; }
        }
        if (failure != null || restorationError != null || d.Dirty != dirty)
        {
            if (result == null && new FileInfo(path).Length == 0) File.Delete(path);
            return DrawingSupport.Success(ctx, new JObject { ["ok"] = false, ["path"] = File.Exists(path) ? path : null, ["error"] = failure?.Message ?? restorationError ?? "Capture changed drawing dirty state.", ["ui_restored"] = restorationError == null, ["readback_required"] = true });
        }
        result!["ui_restored"] = true; result["document_unchanged"] = true; return DrawingSupport.Success(ctx, result);
    }
}
#endif
