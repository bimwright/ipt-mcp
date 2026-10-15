#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

internal static class SketchPointReferences
{
    internal static string Key(PlanarSketch sketch)
    { byte[] key = Array.Empty<byte>(); sketch.GetReferenceKey(ref key, 0); return Convert.ToBase64String(key); }
    internal static string Key(SketchPoint point)
    { byte[] key = Array.Empty<byte>(); point.GetReferenceKey(ref key, 0); return Convert.ToBase64String(key); }
    internal static string Token(PartDocument part, PlanarSketch sketch, string pointKey)
        => new SketchPointToken(part.InternalName, part.FullFileName, Key(sketch), pointKey).Encode();

    internal static PlanarSketch ResolveSketch(PartDocument part, SketchPointToken token)
    {
        if (token.DocumentId != part.InternalName || !string.Equals(token.DocumentPath, part.FullFileName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Point reference belongs to another document or file path; refresh inventor_list_sketch_points");
        var sketch = Bind(part, token.SketchKey) as PlanarSketch;
        if (sketch is null || Key(sketch) != token.SketchKey) throw new ArgumentException("Sketch reference is stale or ambiguous");
        return sketch;
    }

    internal static SketchPoint ResolvePoint(PartDocument part, PlanarSketch sketch, SketchPointToken token)
    {
        if (token.PointKey == "origin")
        {
            var origin = sketch.OriginPoint;
            if (origin is not null)
            {
                foreach (SketchPoint candidate in sketch.SketchPoints)
                    if (candidate.Reference && candidate.ReferencedEntity is object referenced && referenced.Equals(origin)) return candidate;
                if (sketch.AddByProjectingEntity(origin) is not SketchPoint projected)
                    throw new ArgumentException("Sketch origin could not be projected as a point");
                return projected;
            }
            // Plane-based sketches can have no OriginPoint object. Anchor their local (0,0)
            // explicitly. Both this point and its constraint belong to the caller's transaction.
            const string originTag = "Bimwright_PositionalDimensionOrigin";
            foreach (SketchPoint candidate in sketch.SketchPoints)
                if (candidate.AttributeSets.NameIsUsed[originTag])
                {
                    if (candidate.ConstraintStatus != ConstraintStatusEnum.kFullyConstrainedConstraintStatus
                        || Math.Abs(candidate.Geometry.X) > 1e-9 || Math.Abs(candidate.Geometry.Y) > 1e-9)
                        throw new ArgumentException("The dimension origin anchor has been modified; restore it before adding origin dimensions");
                    return candidate;
                }
            var app = (Application)part.Parent;
            var anchor = sketch.SketchPoints.Add(app.TransientGeometry.CreatePoint2d(0, 0), false);
            sketch.GeometricConstraints.AddGround((SketchEntity)(object)anchor);
            anchor.AttributeSets.Add(originTag);
            return anchor;
        }
        var point = Bind(part, token.PointKey) as SketchPoint;
        if (point is null || Key(point) != token.PointKey || point.Parent is not PlanarSketch parent || Key(parent) != token.SketchKey)
            throw new ArgumentException("Point reference is stale, ambiguous or outside the selected sketch; refresh inventor_list_sketch_points");
        return point;
    }

    private static object Bind(PartDocument part, string encoded)
    {
        var key = Convert.FromBase64String(encoded);
        try { object match; return part.ReferenceKeyManager.BindKeyToObject(ref key, 0, out match); }
        catch (Exception) { throw new ArgumentException("Reference no longer resolves; refresh inventor_list_sketch_points"); }
    }
}
#endif
