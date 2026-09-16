#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// <c>derive_envelope</c> — create a new part document containing a derived-component feature that
/// references a source part or assembly (spec F4-P2-1: the envelope/interop path that used to take
/// several send_code scripts). Supports per-solid inclusion (<c>include_bodies</c>) and
/// bounding-box derivation (<c>bounding_box</c>) for lightweight downstream envelopes. The new
/// document is saved to <c>output_path</c> (.ipt, under an allowed root); <c>activate=false</c>
/// keeps it invisible.
/// </summary>
public sealed class DeriveEnvelopeHandler : HandlerBase, IInventorCommand
{
    public string Name => "derive_envelope";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        var outputPath = (p["output_path"]?.Type == JTokenType.String) ? (string)p["output_path"]! : "";
        if (ExportPathPolicy.TryRejectPath(outputPath, out var pathRejection))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, pathRejection);
        if (!outputPath.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "output_path must end in .ipt");

        var sourcePath = (p["source_path"]?.Type == JTokenType.String) ? ((string)p["source_path"]!).Trim() : "";
        if (sourcePath.Length == 0)
        {
            global::Inventor.Document? active;
            try { active = app.ActiveDocument; } catch { active = null; }
            if (active is null)
                return Fail(ctx, InventorErrorCodes.NO_DOCUMENT,
                    "no source_path given and no active Inventor document to derive");
            try { sourcePath = active.FullFileName ?? ""; } catch { sourcePath = ""; }
            if (sourcePath.Length == 0)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    "the active document has never been saved — save it or pass source_path explicitly");
        }
        else if (!System.IO.File.Exists(sourcePath))
        {
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"source_path does not exist: {sourcePath}");
        }

        var deriveStyle = ((string?)p["derive_style"] ?? "multiple").Trim().ToLowerInvariant();
        DerivedComponentStyleEnum style;
        switch (deriveStyle)
        {
            case "multiple": style = DerivedComponentStyleEnum.kDeriveAsMultipleBodies; break;
            case "single_seams": style = DerivedComponentStyleEnum.kDeriveAsSingleBodyWithSeams; break;
            case "single_no_seams": style = DerivedComponentStyleEnum.kDeriveAsSingleBodyNoSeams; break;
            default:
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    $"unknown derive_style '{deriveStyle}' (multiple|single_seams|single_no_seams)");
        }

        var boundingBox = p["bounding_box"]?.Value<bool>() ?? false;
        var includeParams = p["include_parameters"]?.Value<bool>() ?? false;
        var orientedMin = p["use_oriented_min_bounding_box"]?.Value<bool>() ?? false;
        var activate = p["activate"]?.Value<bool>() ?? true;
        var includeBodies = p["include_bodies"] as JArray;
        if (boundingBox && includeBodies is { Count: > 0 })
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "bounding_box and include_bodies are incompatible (bounding box already covers every solid)");

        try
        {
            var tpl = app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
            var doc = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, tpl, activate);
            var defs = doc.ComponentDefinition.ReferenceComponents.DerivedPartComponents;
            var def = (DerivedPartDefinition)defs.CreateDefinition(sourcePath);
            def.DeriveStyle = style;
            def.IncludeAllParameters = includeParams;
            def.UseOrientedMinimumBoundingBox = orientedMin;

            int solidsTotal = 0, solidsIncluded = 0;
            if (boundingBox)
            {
                def.IncludeAllSolids = DerivedComponentOptionEnum.kDerivedBoundingBox;
            }
            else if (includeBodies is { Count: > 0 })
            {
                def.IncludeAllSolids = DerivedComponentOptionEnum.kDerivedIndividualDefined;
                solidsIncluded = ApplyIncludeBodies(def, includeBodies, out solidsTotal);
            }
            else
            {
                def.IncludeAllSolids = DerivedComponentOptionEnum.kDerivedIncludeAll;
            }
            if (solidsTotal == 0) solidsTotal = def.Solids.Count;
            if (solidsIncluded == 0 && !boundingBox && includeBodies is not { Count: > 0 })
                solidsIncluded = solidsTotal;
            if (boundingBox) solidsIncluded = solidsTotal;

            var component = defs.Add(def);
            doc.SaveAs(outputPath, false);

            int bodyCount = 0;
            try { bodyCount = doc.ComponentDefinition.SurfaceBodies.Count; } catch { }

            return Ok(ctx, new JObject
            {
                ["document_title"] = doc.DisplayName,
                ["output_path"] = outputPath,
                ["saved"] = System.IO.File.Exists(outputPath),
                ["source_path"] = sourcePath,
                ["derive_style"] = deriveStyle,
                ["bounding_box"] = boundingBox,
                ["solids_total"] = solidsTotal,
                ["solids_included"] = solidsIncluded,
                ["derived_component"] = component.Name,
                ["body_count"] = bodyCount,
                ["active"] = activate,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, "failed to derive envelope: " + ex.Message); }
    }

    /// <summary>
    /// Apply <c>include_bodies</c> tokens (body names or <c>body:N</c>/bare-N 1-based indices into the
    /// definition's Solids collection) by setting each <see cref="DerivedPartEntity.IncludeEntity"/>.
    /// Throws <see cref="ArgumentException"/> listing the available solid names when a token matches
    /// nothing. Returns the number of solids left included.
    /// </summary>
    private static int ApplyIncludeBodies(DerivedPartDefinition def, JArray tokens, out int solidsTotal)
    {
        var solids = def.Solids;
        solidsTotal = solids.Count;
        var names = new List<string>(solidsTotal);
        foreach (DerivedPartEntity e in solids)
        {
            var sb = e.ReferencedEntity as SurfaceBody;
            names.Add(sb?.Name ?? "");
        }

        var wanted = new HashSet<int>();
        var unmatched = new List<string>();
        foreach (var t in tokens)
        {
            var s = t.ToString().Trim();
            var idx = -1;
            if (int.TryParse(s, out var n)) idx = n;
            else if (s.StartsWith("body:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(s.Substring(5), out n)) idx = n;
            else
            {
                var ni = names.FindIndex(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase));
                if (ni >= 0) idx = ni + 1;
            }
            if (idx < 1 || idx > solidsTotal) unmatched.Add(s); else wanted.Add(idx);
        }
        if (unmatched.Count > 0)
            throw new ArgumentException(
                $"unknown bodies: {string.Join(", ", unmatched)} (available: {string.Join(", ", names)})");

        var i = 0;
        foreach (DerivedPartEntity e in solids)
        {
            i++;
            e.IncludeEntity = wanted.Contains(i);
        }
        return wanted.Count;
    }
}
#endif
