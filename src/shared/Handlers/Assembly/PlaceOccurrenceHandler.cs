#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public sealed class PlaceOccurrenceHandler : HandlerBase, IInventorCommand
{
    public string Name => "place_occurrence";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext context, JObject parameters)
    {
        if (!ActiveDocumentSupport.TryGetActiveAssembly(context, Name, out var app, out var assemblyDoc, out var failure))
        {
            return failure!;
        }

        string path = (string?)parameters["path"] ?? "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail(context, "INVALID_ARGUMENT", "path is required");
        }

        bool grounded = (bool?)parameters["grounded"] ?? false;
        var position = parameters["position_mm"] as JArray;
        var rotation = parameters["rotation_deg_xyz"] as JArray;

        try
        {
            var matrix = app.TransientGeometry.CreateMatrix();

            if (position != null || rotation != null)
            {
                double px = 0, py = 0, pz = 0;
                if (position != null && position.Count == 3)
                {
                    px = (double)position[0] / 10.0; // cm
                    py = (double)position[1] / 10.0;
                    pz = (double)position[2] / 10.0;
                }
                matrix.SetTranslation(app.TransientGeometry.CreateVector(px, py, pz));

                if (rotation != null && rotation.Count == 3)
                {
                    double rx = (double)rotation[0] * Math.PI / 180.0; // rad
                    double ry = (double)rotation[1] * Math.PI / 180.0;
                    double rz = (double)rotation[2] * Math.PI / 180.0;

                    var rotX = app.TransientGeometry.CreateMatrix();
                    rotX.SetToRotation(rx, app.TransientGeometry.CreateVector(1, 0, 0), app.TransientGeometry.CreatePoint(0, 0, 0));
                    var rotY = app.TransientGeometry.CreateMatrix();
                    rotY.SetToRotation(ry, app.TransientGeometry.CreateVector(0, 1, 0), app.TransientGeometry.CreatePoint(0, 0, 0));
                    var rotZ = app.TransientGeometry.CreateMatrix();
                    rotZ.SetToRotation(rz, app.TransientGeometry.CreateVector(0, 0, 1), app.TransientGeometry.CreatePoint(0, 0, 0));

                    matrix.PostMultiplyBy(rotX);
                    matrix.PostMultiplyBy(rotY);
                    matrix.PostMultiplyBy(rotZ);
                }
            }

            var occ = assemblyDoc.ComponentDefinition.Occurrences.Add(path, matrix);
            occ.Grounded = grounded;

            var min = occ.RangeBox.MinPoint;
            var max = occ.RangeBox.MaxPoint;
            var bbox = new JObject
            {
                ["min"] = new JArray(min.X * 10.0, min.Y * 10.0, min.Z * 10.0),
                ["max"] = new JArray(max.X * 10.0, max.Y * 10.0, max.Z * 10.0)
            };

            return Ok(context, new JObject
            {
                ["occurrence_name"] = occ.Name,
                ["bbox_mm"] = bbox
            });
        }
        catch (Exception ex)
        {
            return Fail(context, "API_ERROR", "Failed to place occurrence: " + ex.Message);
        }
    }
}
#endif
