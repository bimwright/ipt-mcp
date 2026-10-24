#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;
using File = System.IO.File;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>export_bodies_sat</c> — writes each selected solid body of one or more part documents already
/// in memory to its own ACIS SAT file (<c>&lt;key&gt;-bNNN.sat</c>, NNN = 1-based body index) via
/// <see cref="SurfaceBody.DataIO"/>, the per-body Revit interop path that <c>export_sat</c> (whole
/// document, active only) cannot cover. Everything is validated before the first write: documents,
/// body refs, output paths under <see cref="ExportPathPolicy"/>, duplicate targets and existing files
/// (unless <c>overwrite</c>). A body that fails to write or verify is reported and the rest continue,
/// so a retry only needs the failed bodies. <c>verify</c> reads each file back with
/// <see cref="TransientBRep.ReadFromFile"/> and compares body count, face count, volume and range box.
/// Does not mutate the documents.
/// </summary>
public sealed class ExportBodiesSatHandler : HandlerBase, IInventorCommand
{
    public string Name => "export_bodies_sat";
    public bool IsReadOnly => true;

    internal const int MaxItems = 200;
    internal const int MaxBodies = 2000;
    private const double VolumeAccuracy = 0.001;
    private const double VolumeRelTolerance = 1e-4;
    private const double BBoxToleranceMm = 0.01;

    private sealed record Target(string Document, string Key, int Index, SurfaceBody Body, string File);

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (p["items"] is not JArray items || items.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "items must be a non-empty array of {document, bodies?, key?}");
        if (items.Count > MaxItems)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"at most {MaxItems} items per call");
        var outDir = p["out_dir"]?.Type == JTokenType.String ? (string)p["out_dir"]! : "";
        if (string.IsNullOrWhiteSpace(outDir) || !Path.IsPathRooted(outDir))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "out_dir must be an absolute directory path");
        var verify = p["verify"]?.Type != JTokenType.Boolean || (bool)p["verify"]!;
        var overwrite = p["overwrite"]?.Type == JTokenType.Boolean && (bool)p["overwrite"]!;

        // ---- preflight: resolve everything before writing anything ----
        var targets = new List<Target>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not JObject item)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"items[{i}] must be an object");
            if (!ActiveDocumentSupport.TryGetPart(ctx, item, "export_bodies_sat", out _, out var part, out var failure))
                return failure!;
            var bodies = part.ComponentDefinition.SurfaceBodies;
            string docPath = "";
            try { docPath = part.FullFileName; } catch { }
            var key = item["key"]?.Type == JTokenType.String ? (string)item["key"]! : Path.GetFileNameWithoutExtension(docPath);
            if (string.IsNullOrWhiteSpace(key)) key = part.DisplayName;
            if (key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"items[{i}].key '{key}' contains characters not allowed in a file name");

            var indices = new List<int>();
            if (item["bodies"] is JArray refs && refs.Count > 0)
            {
                foreach (var r in refs)
                {
                    try { indices.Add(BodyIndex(part.ComponentDefinition, (string?)r ?? "")); }
                    catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"items[{i}]: {ex.Message}"); }
                }
            }
            else for (var b = 1; b <= bodies.Count; b++) indices.Add(b);
            if (indices.Count == 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"items[{i}] ({key}) has no solid bodies");

            foreach (var index in indices.Distinct())
            {
                var file = Path.Combine(outDir, $"{key}-b{index:000}.sat");
                if (ExportPathPolicy.TryRejectPath(file, out var rejection))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, rejection);
                if (!files.Add(file))
                    return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"duplicate output file {Path.GetFileName(file)} — give the items distinct keys");
                targets.Add(new Target(string.IsNullOrEmpty(docPath) ? part.DisplayName : docPath, key, index, bodies[index], file));
            }
            if (targets.Count > MaxBodies)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"at most {MaxBodies} bodies per call; split the items");
        }
        if (!overwrite)
        {
            var existing = targets.Where(t => File.Exists(t.File)).Select(t => Path.GetFileName(t.File)).ToArray();
            if (existing.Length > 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    $"{existing.Length} output file(s) already exist (e.g. {string.Join(", ", existing.Take(3))}); pass overwrite=true or another out_dir");
        }
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "cannot create out_dir: " + ex.Message); }

        // ---- write (and verify) each body; one failure does not stop the rest ----
        var rows = new JArray();
        int exported = 0, verified = 0, failed = 0;
        foreach (var t in targets)
        {
            var row = new JObject { ["document"] = t.Document, ["key"] = t.Key, ["id"] = $"body:{t.Index}", ["file"] = t.File };
            try
            {
                row["name"] = t.Body.Name;
                var source = Signature(t.Body);
                row["volume_mm3"] = source.Volume;
                row["bbox_mm"] = BBox(source.Box);
                row["face_count"] = source.Faces;
                t.Body.DataIO.WriteDataToFile("ACIS SAT", t.File);
                exported++;
                if (verify)
                {
                    var check = Verify(app, t.File, source);
                    row["verify"] = check;
                    if ((bool)check["ok"]!) verified++; else failed++;
                }
            }
            catch (Exception ex)
            {
                row["error"] = ex.GetBaseException().Message;
                failed++;
            }
            rows.Add(row);
        }

        return Ok(ctx, new JObject
        {
            ["ok"] = failed == 0,
            ["out_dir"] = outDir,
            ["requested"] = targets.Count,
            ["exported"] = exported,
            ["verified"] = verify ? verified : JValue.CreateNull(),
            ["failed"] = failed,
            ["tolerances"] = verify ? new JObject { ["volume_rel"] = VolumeRelTolerance, ["bbox_mm"] = BBoxToleranceMm } : null,
            ["bodies"] = rows,
        });
    }

    private sealed record Sig(double Volume, double[] Box, int Faces);

    private static Sig Signature(SurfaceBody body)
    {
        var rb = body.RangeBox;
        return new Sig(UnitConvert.Cm3ToMm3(body.get_Volume(VolumeAccuracy)), new[]
        {
            UnitConvert.CmToMm(rb.MinPoint.X), UnitConvert.CmToMm(rb.MinPoint.Y), UnitConvert.CmToMm(rb.MinPoint.Z),
            UnitConvert.CmToMm(rb.MaxPoint.X), UnitConvert.CmToMm(rb.MaxPoint.Y), UnitConvert.CmToMm(rb.MaxPoint.Z),
        }, body.Faces.Count);
    }

    private static JObject Verify(Application app, string file, Sig source)
    {
        var read = app.TransientBRep.ReadFromFile(file);
        var count = read.Count;
        var check = new JObject { ["bodies_read"] = count };
        if (count != 1)
        {
            check["ok"] = false;
            check["reason"] = $"expected 1 body in the file, read {count}";
            return check;
        }
        var back = Signature(read[1]);
        var volumeError = Math.Abs(back.Volume - source.Volume) / Math.Max(Math.Abs(source.Volume), 1e-9);
        var bboxError = source.Box.Zip(back.Box, (a, b) => Math.Abs(a - b)).Max();
        check["volume_rel_error"] = volumeError;
        check["bbox_error_mm"] = bboxError;
        check["face_count"] = back.Faces;
        var ok = volumeError <= VolumeRelTolerance && bboxError <= BBoxToleranceMm && back.Faces == source.Faces;
        check["ok"] = ok;
        if (!ok) check["reason"] = "read-back geometry differs from the source body beyond tolerance";
        return check;
    }

    private static JObject BBox(double[] b) => new()
    {
        ["min"] = new JArray(b[0], b[1], b[2]),
        ["max"] = new JArray(b[3], b[4], b[5]),
    };

    /// <summary>1-based index for the body refs <see cref="EntityResolver.ResolveBody"/> accepts ("N", "body:N" or name).</summary>
    private static int BodyIndex(PartComponentDefinition def, string bodyRef)
    {
        var bodies = def.SurfaceBodies;
        var s = bodyRef.Trim();
        var head = s.StartsWith("body:", StringComparison.OrdinalIgnoreCase) ? s.Substring(5) : s;
        if (int.TryParse(head, out var idx))
        {
            if (idx < 1 || idx > bodies.Count)
                throw new ArgumentException($"body index {idx} out of range (1..{bodies.Count})");
            return idx;
        }
        for (var i = 1; i <= bodies.Count; i++)
            if (string.Equals(bodies[i].Name, s, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new ArgumentException($"unknown body '{bodyRef}' (use a 1-based index, body:N, or the body name)");
    }
}
#endif
