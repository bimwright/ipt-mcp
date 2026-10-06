#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// Shared feature-handler helpers: profile creation, operation/direction enum mapping, and edge
/// collection building.
/// </summary>
internal static class FeatureSupport
{
    /// <summary>
    /// Build a solid profile from a named sketch. If the sketch already has a computed profile we use
    /// the first; otherwise we compute one via <c>Profiles.AddForSolid()</c> (which auto-detects the
    /// closed loops). Throws <see cref="ArgumentException"/> with a friendly message when the sketch
    /// is missing or has no closed profile.
    /// </summary>
    public static Profile SolidProfile(PartComponentDefinition def, string sketchName)
    {
        var sketch = Bimwright.Ipt.Shared.Handlers.EntityResolver.FindSketch(def, sketchName)
            ?? throw new ArgumentException($"no sketch named '{sketchName}'");
        if (sketch.Profiles.Count > 0)
            return sketch.Profiles[1];
        try { return sketch.Profiles.AddForSolid(true, null, null); }
        catch (Exception ex) { throw new ArgumentException($"sketch '{sketchName}' has no closed profile to use: {ex.Message}"); }
    }

    /// <summary>
    /// Resolve a profile by spec — <c>"SketchName"</c> (first profile) or <c>"SketchName:N"</c>
    /// (1-based profile index for multi-profile sketches). Empty profile collections are computed
    /// via <c>AddForSolid</c> first, mirroring <see cref="SolidProfile"/>.
    /// </summary>
    public static Profile ProfileOf(PartComponentDefinition def, string spec)
    {
        var s = (spec ?? "").Trim();
        var idx = 1;
        var colon = s.LastIndexOf(':');
        if (colon > 0)
        {
            if (!int.TryParse(s.Substring(colon + 1), out idx) || idx < 1)
                throw new ArgumentException($"invalid profile spec '{spec}' (use 'SketchName' or 'SketchName:N')");
            s = s.Substring(0, colon);
        }
        var sketch = Bimwright.Ipt.Shared.Handlers.EntityResolver.FindSketch(def, s)
            ?? throw new ArgumentException($"no sketch named '{s}'");
        if (sketch.Profiles.Count == 0)
        {
            try { sketch.Profiles.AddForSolid(true, null, null); } catch { }
        }
        if (idx > sketch.Profiles.Count)
            throw new ArgumentException($"sketch '{s}' has no closed profile at index {idx} (1..{sketch.Profiles.Count})");
        return sketch.Profiles[idx];
    }

    /// <summary>
    /// Volume (mm³) of the last body in a feature's SurfaceBodies — the body the feature produced
    /// or last affected — falling back to total part volume when the feature reports none.
    /// Shared by extrude/loft/sweep responses (see the F5 finding on extrude volume_mm3).
    /// </summary>
    public static double FeatureBodyVolumeMm3(PartComponentDefinition def, SurfaceBodies featureBodies)
    {
        try
        {
            if (featureBodies.Count > 0)
                return UnitConvert.Cm3ToMm3(featureBodies[featureBodies.Count].get_Volume(0.01));
        }
        catch { }
        double cm3 = 0;
        try { foreach (SurfaceBody b in def.SurfaceBodies) cm3 += b.get_Volume(0.01); } catch { }
        return UnitConvert.Cm3ToMm3(cm3);
    }

    /// <summary>
    /// First non-construction curve entity in a sketch — used for sweep paths and loft centerlines
    /// (the interop has no <c>SketchCurve</c> type; <c>CreatePath</c>/<c>Centerline</c> take the entity
    /// as <c>Object</c>). Skips construction entities and <see cref="SketchPoint"/>s, and throws
    /// <see cref="ArgumentException"/> when the sketch has no usable curves.
    /// </summary>
    public static SketchEntity FirstSketchCurve(PlanarSketch sketch, string role, string sketchName)
    {
        foreach (SketchEntity e in sketch.SketchEntities)
            if (!e.Construction && e is not SketchPoint) return e;
        throw new ArgumentException($"{role} sketch '{sketchName}' has no usable curves");
    }

    /// <summary>Name the body a new_body feature produced, mirroring extrude's convention.</summary>
    public static string? NameNewBody(PartComponentDefinition def, string featureName)
    {
        try
        {
            if (def.SurfaceBodies.Count < 1) return null;
            var body = def.SurfaceBodies[def.SurfaceBodies.Count];
            try { body.Name = featureName + "_body"; } catch { }
            return body.Name;
        }
        catch { return null; }
    }

    public static PartFeatureOperationEnum Operation(string? op) => (op ?? "join").Trim().ToLowerInvariant() switch
    {
        "join" => PartFeatureOperationEnum.kJoinOperation,
        "cut" => PartFeatureOperationEnum.kCutOperation,
        "intersect" => PartFeatureOperationEnum.kIntersectOperation,
        "newbody" or "new_body" => PartFeatureOperationEnum.kNewBodyOperation,
        _ => throw new ArgumentException($"unknown operation '{op}' (join|cut|intersect|new_body)"),
    };

    public static PartFeatureExtentDirectionEnum Direction(string? dir) => (dir ?? "positive").Trim().ToLowerInvariant() switch
    {
        "positive" => PartFeatureExtentDirectionEnum.kPositiveExtentDirection,
        "negative" => PartFeatureExtentDirectionEnum.kNegativeExtentDirection,
        "symmetric" => PartFeatureExtentDirectionEnum.kSymmetricExtentDirection,
        _ => throw new ArgumentException($"unknown direction '{dir}' (positive|negative|symmetric)"),
    };

    /// <summary>Build an EdgeCollection from a list of edge ids resolved against the part definition.</summary>
    public static EdgeCollection EdgeCollection(Application app, PartComponentDefinition def, Newtonsoft.Json.Linq.JArray edgeIds)
    {
        var col = app.TransientObjects.CreateEdgeCollection();
        foreach (var token in edgeIds)
            col.Add(Bimwright.Ipt.Shared.Handlers.EntityResolver.ResolveEdge(def, token.ToString()));
        return col;
    }

    /// <summary>
    /// Resolve a planar reference for work-feature construction: <c>XY/XZ/YZ</c> origin planes, a work
    /// plane by 1-based id or name, or a planar face (<c>face:F</c> / <c>body:B/face:F</c>).
    /// </summary>
    public static object ResolvePlaneRef(PartComponentDefinition def, string r)
    {
        switch (r.Trim().ToUpperInvariant())
        {
            case "XY": return def.WorkPlanes["XY Plane"];
            case "XZ": return def.WorkPlanes["XZ Plane"];
            case "YZ": return def.WorkPlanes["YZ Plane"];
        }
        if (Bimwright.Ipt.Shared.Handlers.EntityResolver.IsEntityRef(r, "face"))
            return ResolveFaceRef(def, r);
        var planes = def.WorkPlanes;
        if (int.TryParse(r, out var idx))
        {
            if (idx < 1 || idx > planes.Count) throw new ArgumentException($"work plane index {idx} out of range (1..{planes.Count})");
            return planes[idx];
        }
        try { return planes[r]; }
        catch { throw new ArgumentException($"unknown plane reference '{r}'"); }
    }

    public static Face ResolveFaceRef(PartComponentDefinition def, string r)
    {
        var bodies = def.SurfaceBodies;
        if (bodies.Count < 1) throw new ArgumentException("the part has no solid bodies");
        var b = Bimwright.Ipt.Shared.Handlers.EntityResolver.ParseBodyIndex(r);
        if (b > bodies.Count) throw new ArgumentException($"body index {b} out of range");
        var faces = bodies[b].Faces;
        var f = Bimwright.Ipt.Shared.Handlers.EntityResolver.ParseIndex(r, "face");
        if (f > faces.Count) throw new ArgumentException($"face index {f} out of range (1..{faces.Count})");
        return faces[f];
    }

    /// <summary>
    /// Resolve a point reference: a named origin work point ("Center Point"), a work point by 1-based
    /// id, or a body vertex (<c>vertex:V</c> / <c>body:B/vertex:V</c>).
    /// </summary>
    public static object ResolvePointRef(PartComponentDefinition def, string r)
    {
        var s = r.Trim();
        if (Bimwright.Ipt.Shared.Handlers.EntityResolver.IsEntityRef(s, "vertex"))
        {
            var bodies = def.SurfaceBodies;
            if (bodies.Count < 1) throw new ArgumentException("the part has no solid bodies");
            var b = Bimwright.Ipt.Shared.Handlers.EntityResolver.ParseBodyIndex(s);
            if (b > bodies.Count) throw new ArgumentException($"body index {b} out of range");
            var verts = bodies[b].Vertices;
            var v = Bimwright.Ipt.Shared.Handlers.EntityResolver.ParseIndex(s, "vertex");
            if (v > verts.Count) throw new ArgumentException($"vertex index {v} out of range (1..{verts.Count})");
            return verts[v];
        }
        var pts = def.WorkPoints;
        if (int.TryParse(s, out var idx))
        {
            if (idx < 1 || idx > pts.Count) throw new ArgumentException($"work point index {idx} out of range (1..{pts.Count})");
            return pts[idx];
        }
        try { return pts[s]; }
        catch { throw new ArgumentException($"unknown point reference '{r}' (use a work-point id/name or vertex:N)"); }
    }
}
#endif
