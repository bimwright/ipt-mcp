#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;

namespace Bimwright.Inventor.Shared.Handlers.Sketch;

/// <summary>
/// <c>create_sketch</c> — create a new 2D sketch on an origin plane (XY|XZ|YZ) or on a referenced
/// planar face / work plane. STA-bound. Returns the new sketch name so subsequent draw/dimension
/// tools can target it by name.
/// </summary>
public sealed class CreateSketchHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_sketch";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not PartDocument part)
            return Fail(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "create_sketch requires an active part document");

        var plane = (p["plane"]?.ToString() ?? "XY").Trim();
        if (string.IsNullOrEmpty(plane))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "plane is required");

        var def = part.ComponentDefinition;
        object planarEntity;
        try
        {
            planarEntity = ResolvePlane(def, plane);
        }
        catch (ArgumentException ex)
        {
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message);
        }

        try
        {
            var sketch = def.Sketches.Add(planarEntity, false);
            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["plane"] = plane,
            });
        }
        catch (Exception ex)
        {
            return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message);
        }
    }

    /// <summary>
    /// Resolve a planar entity for sketch creation. <c>XY/XZ/YZ</c> map to the origin work planes
    /// (Inventor names them "XY Plane" etc.); otherwise the value is treated as a work-plane id
    /// (1-based index into WorkPlanes) or a planar face id (<c>body:B/face:F</c>).
    /// </summary>
    private static object ResolvePlane(PartComponentDefinition def, string plane)
    {
        switch (plane.ToUpperInvariant())
        {
            case "XY": return def.WorkPlanes["XY Plane"];
            case "XZ": return def.WorkPlanes["XZ Plane"];
            case "YZ": return def.WorkPlanes["YZ Plane"];
        }

        // face reference: "face:F" / "body:B/face:F"
        if (plane.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var bodyIdx = EntityResolver.ParseBodyIndex(plane);
            var bodies = def.SurfaceBodies;
            if (bodyIdx > bodies.Count) throw new ArgumentException($"body index {bodyIdx} out of range");
            var faces = bodies[bodyIdx].Faces;
            var faceIdx = EntityResolver.ParseIndex(plane, "face");
            if (faceIdx > faces.Count) throw new ArgumentException($"face index {faceIdx} out of range (1..{faces.Count})");
            return faces[faceIdx];
        }

        // work plane by id (1-based index) or by name
        var planes = def.WorkPlanes;
        if (int.TryParse(plane, out var wpIdx))
        {
            if (wpIdx < 1 || wpIdx > planes.Count) throw new ArgumentException($"work plane index {wpIdx} out of range (1..{planes.Count})");
            return planes[wpIdx];
        }
        try { return planes[plane]; }
        catch { throw new ArgumentException($"unknown plane reference '{plane}' (use XY|XZ|YZ, a work-plane id/name, or face:N)"); }
    }
}
#endif
