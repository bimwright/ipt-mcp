#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class CreateIMateHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_imate";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(context, Name, out var app, out var partDoc, out var failure))
        {
            return failure!;
        }

        string name = (string?)parameters["name"] ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fail(context, "INVALID_ARGUMENT", "name is required");
        }

        string type = ((string?)parameters["type"] ?? "").Trim().ToLowerInvariant();
        if (type is not ("mate" or "flush" or "insert"))
        {
            return Fail(context, "INVALID_ARGUMENT", "type must be mate|flush|insert");
        }

        var selObj = parameters["selector"] as JObject;
        FaceSelectorSpec? spec = null;
        string specErr = "required";
        bool parsed = false;
        if (selObj != null)
        {
            parsed = FaceSelectorSpec.TryParse(selObj, out spec, out specErr);
        }

        if (!parsed || spec == null)
        {
            return Fail(context, "INVALID_ARGUMENT", "Invalid selector: " + specErr);
        }

        double offsetMm = (double?)parameters["offset_mm"] ?? 0;
        bool insertOpposed = (bool?)parameters["insert_opposed"] ?? true;
        double distanceMm = (double?)parameters["distance_mm"] ?? 0;

        var def = partDoc.ComponentDefinition;

        // Check duplicate name
        foreach (iMateDefinition existing in def.iMateDefinitions)
        {
            if (string.Equals(existing.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Fail(context, "INVALID_ARGUMENT", "Duplicate iMate name: " + name);
            }
        }

        // Select the face
        var face = FaceSelector.SelectFace(def, spec, out var candidates);
        if (face == null)
        {
            string errMessage = candidates.Count == 0
                ? "Face selection matches 0 faces."
                : "Face selection is ambiguous (matches " + candidates.Count + " faces).";

            var failResult = InventorCommandResult.Fail(System.Guid.Empty, "INVALID_ARGUMENT", errMessage, new InventorResponseMeta
            {
                TargetId = context.TargetId,
                InventorYear = context.InventorYear == 0 ? (int?)null : context.InventorYear
            });
            failResult.Data = new JObject { ["candidates"] = candidates };
            return failResult;
        }

        try
        {
            iMateDefinition im;
            double offsetCm = UnitConvert.MmToCm(offsetMm);
            double distanceCm = UnitConvert.MmToCm(distanceMm);

            switch (type)
            {
                case "mate":
                    im = (iMateDefinition)def.iMateDefinitions.AddMateiMateDefinition(face, offsetCm);
                    break;
                case "flush":
                    im = (iMateDefinition)def.iMateDefinitions.AddFlushiMateDefinition(face, offsetCm);
                    break;
                case "insert":
                    im = (iMateDefinition)def.iMateDefinitions.AddInsertiMateDefinition(face, insertOpposed, distanceCm);
                    break;
                default:
                    return Fail(context, "INVALID_ARGUMENT", "Unsupported iMate type: " + type);
            }

            im.Name = name.Trim();

            // Matched face metadata for output
            var range = face.Evaluator.RangeBox;
            var min = range.MinPoint;
            var max = range.MaxPoint;
            var centroid = new JArray(
                UnitConvert.CmToMm((min.X + max.X) / 2.0),
                UnitConvert.CmToMm((min.Y + max.Y) / 2.0),
                UnitConvert.CmToMm((min.Z + max.Z) / 2.0)
            );

            var matchedFace = new JObject
            {
                ["kind"] = spec.Kind,
                ["centroid_mm"] = centroid
            };

            if (spec.Kind == "planar")
            {
                var plane = (Plane)face.Geometry;
                double nx = plane.Normal.X;
                double ny = plane.Normal.Y;
                double nz = plane.Normal.Z;
                if (face.IsParamReversed)
                {
                    nx = -nx; ny = -ny; nz = -nz;
                }
                matchedFace["normal"] = new JArray(nx, ny, nz);
            }
            else
            {
                var cyl = (Cylinder)face.Geometry;
                matchedFace["radius_mm"] = UnitConvert.CmToMm(cyl.Radius);
                matchedFace["axis"] = new JArray(cyl.AxisVector.X, cyl.AxisVector.Y, cyl.AxisVector.Z);
            }

            return Ok(context, new JObject
            {
                ["imate_name"] = im.Name,
                ["type"] = type,
                ["matched_face"] = matchedFace
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to create iMate: " + ex.Message);
        }
    }
}
#endif
