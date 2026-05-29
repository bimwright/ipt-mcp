#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Feature;

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
        var sketch = Bimwright.Inventor.Shared.Handlers.EntityResolver.FindSketch(def, sketchName)
            ?? throw new ArgumentException($"no sketch named '{sketchName}'");
        if (sketch.Profiles.Count > 0)
            return sketch.Profiles[1];
        try { return sketch.Profiles.AddForSolid(true, null, null); }
        catch (Exception ex) { throw new ArgumentException($"sketch '{sketchName}' has no closed profile to use: {ex.Message}"); }
    }

    public static PartFeatureOperationEnum Operation(string? op) => (op ?? "join").Trim().ToLowerInvariant() switch
    {
        "join" => PartFeatureOperationEnum.kJoinOperation,
        "cut" => PartFeatureOperationEnum.kCutOperation,
        "intersect" => PartFeatureOperationEnum.kIntersectOperation,
        "newbody" or "new_body" => PartFeatureOperationEnum.kNewBodyOperation,
        _ => throw new ArgumentException($"unknown operation '{op}' (join|cut|intersect)"),
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
            col.Add(Bimwright.Inventor.Shared.Handlers.EntityResolver.ResolveEdge(def, token.ToString()));
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
        if (r.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)
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
        var b = Bimwright.Inventor.Shared.Handlers.EntityResolver.ParseBodyIndex(r);
        if (b > bodies.Count) throw new ArgumentException($"body index {b} out of range");
        var faces = bodies[b].Faces;
        var f = Bimwright.Inventor.Shared.Handlers.EntityResolver.ParseIndex(r, "face");
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
        if (s.IndexOf("vertex", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var bodies = def.SurfaceBodies;
            if (bodies.Count < 1) throw new ArgumentException("the part has no solid bodies");
            var b = Bimwright.Inventor.Shared.Handlers.EntityResolver.ParseBodyIndex(s);
            if (b > bodies.Count) throw new ArgumentException($"body index {b} out of range");
            var verts = bodies[b].Vertices;
            var v = Bimwright.Inventor.Shared.Handlers.EntityResolver.ParseIndex(s, "vertex");
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
