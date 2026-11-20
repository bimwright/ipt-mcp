#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;

namespace Bimwright.Ipt.Scripting;

public static partial class Ipt
{
    /// <summary>An axis-aligned box in mm (cleaned to 1e-6 mm). Serializes as plain numbers when returned.</summary>
    public sealed class Bbox
    {
        public double MinX { get; internal set; }
        public double MinY { get; internal set; }
        public double MinZ { get; internal set; }
        public double MaxX { get; internal set; }
        public double MaxY { get; internal set; }
        public double MaxZ { get; internal set; }
        public double SizeX => Clean(MaxX - MinX);
        public double SizeY => Clean(MaxY - MinY);
        public double SizeZ => Clean(MaxZ - MinZ);
        public double[] Min => new[] { MinX, MinY, MinZ };
        public double[] Max => new[] { MaxX, MaxY, MaxZ };
        public double[] Center => new[] { Clean((MinX + MaxX) / 2), Clean((MinY + MaxY) / 2), Clean((MinZ + MaxZ) / 2) };

        /// <summary>True when the two boxes overlap after growing each by <paramref name="gapMm"/>.</summary>
        public bool Overlaps(Bbox other, double gapMm = 0) =>
            MinX - gapMm <= other.MaxX && other.MinX - gapMm <= MaxX &&
            MinY - gapMm <= other.MaxY && other.MinY - gapMm <= MaxY &&
            MinZ - gapMm <= other.MaxZ && other.MinZ - gapMm <= MaxZ;

        public override string ToString() =>
            $"X {MinX}..{MaxX}  Y {MinY}..{MaxY}  Z {MinZ}..{MaxZ} mm";
    }

    /// <summary>
    /// Range box in mm of an occurrence, body, face, edge, part/assembly document or definition, or an Inventor Box.
    /// Faces and edges use their evaluator (they have no RangeBox property).
    /// </summary>
    public static Bbox BboxMm(object item)
    {
        var b = RangeBoxOf(item);
        var min = CleanMm(b.MinPoint.X, b.MinPoint.Y, b.MinPoint.Z);
        var max = CleanMm(b.MaxPoint.X, b.MaxPoint.Y, b.MaxPoint.Z);
        return new Bbox { MinX = min[0], MinY = min[1], MinZ = min[2], MaxX = max[0], MaxY = max[1], MaxZ = max[2] };
    }

    /// <summary>[x,y,z] mm (cleaned) of a Point, Vertex or WorkPoint.</summary>
    public static double[] XyzMm(object point)
    {
        var p = RawMm(point);
        return new[] { Clean(p[0]), Clean(p[1]), Clean(p[2]) };
    }

    /// <summary>Volume in mm³ of a body (including a transient one), a part, or an occurrence.</summary>
    public static double VolumeMm3(object item, double accuracy = 0.0001) => item switch
    {
        SurfaceBody b => Clean(b.get_Volume(accuracy) * 1000.0),
        ComponentOccurrence o => Clean(o.MassProperties.Volume * 1000.0),
        _ => Resolve(item) is PartDocument p ? Clean(p.ComponentDefinition.MassProperties.Volume * 1000.0)
            : throw new IptException($"VolumeMm3 needs a body, part or occurrence, got {Describe(item)}."),
    };

    /// <summary>A 3D point from mm.</summary>
    public static Point Pt(double x, double y, double z) => Tg.CreatePoint(Cm(x), Cm(y), Cm(z));

    /// <summary>A 2D point from mm (sketch or sheet coordinates).</summary>
    public static Point2d Pt2(double x, double y) => Tg.CreatePoint2d(Cm(x), Cm(y));

    /// <summary>A point on a drawing sheet from mm (same as <see cref="Pt2"/>).</summary>
    public static Point2d SheetPt(double x, double y) => Pt2(x, y);

    /// <summary>A displacement vector from mm.</summary>
    public static Vector Vec(double x, double y, double z) => Tg.CreateVector(Cm(x), Cm(y), Cm(z));

    /// <summary>A unit direction (unitless; normalized for you).</summary>
    public static UnitVector Dir(double x, double y, double z)
    {
        var l = Math.Sqrt(x * x + y * y + z * z);
        if (l < 1e-12) throw new IptException("Dir needs a non-zero direction.");
        return Tg.CreateUnitVector(x / l, y / l, z / l);
    }

    /// <summary>An ObjectCollection holding the given items (lists are flattened one level).</summary>
    public static ObjectCollection Coll(params object[] items)
    {
        var c = App.TransientObjects.CreateObjectCollection();
        foreach (var item in items)
        {
            if (item is System.Collections.IEnumerable e and not string && !(item.GetType().IsCOMObject))
                foreach (var x in e) c.Add(x);
            else
                c.Add(item);
        }
        return c;
    }

    /// <summary>Solid and surface bodies of a part (name/path/Document), a definition, or an occurrence (as proxies).</summary>
    public static List<SurfaceBody> Bodies(object? source = null)
    {
        SurfaceBodies bodies = source switch
        {
            ComponentOccurrence o => o.SurfaceBodies,
            PartComponentDefinition pd => pd.SurfaceBodies,
            _ => Part(source).ComponentDefinition.SurfaceBodies,
        };
        var list = new List<SurfaceBody>();
        foreach (SurfaceBody b in bodies) list.Add(b);
        return list;
    }

    /// <summary>Faces of a body, or of every body of a part/definition/occurrence.</summary>
    public static List<Face> Faces(object? source = null)
    {
        var list = new List<Face>();
        foreach (var b in source is SurfaceBody body ? new List<SurfaceBody> { body } : Bodies(source))
            foreach (Face f in b.Faces) list.Add(f);
        return list;
    }

    /// <summary>Edges of a face or body, or of every body of a part/definition/occurrence.</summary>
    public static List<Edge> Edges(object? source = null)
    {
        var list = new List<Edge>();
        if (source is Face face)
        {
            foreach (Edge e in face.Edges) list.Add(e);
            return list;
        }
        foreach (var b in source is SurfaceBody body ? new List<SurfaceBody> { body } : Bodies(source))
            foreach (Edge e in b.Edges) list.Add(e);
        return list;
    }

    internal static Box RangeBoxOf(object item) => item switch
    {
        Box b => b,
        ComponentOccurrence o => o.RangeBox,
        SurfaceBody b => b.RangeBox,
        Face f => f.Evaluator.RangeBox,
        Edge e => e.Evaluator.RangeBox,
        PartComponentDefinition pd => pd.RangeBox,
        AssemblyComponentDefinition ad => ad.RangeBox,
        Document or string => Resolve(item) switch
        {
            PartDocument p => p.ComponentDefinition.RangeBox,
            AssemblyDocument a => a.ComponentDefinition.RangeBox,
            var d => throw new IptException($"{Describe(d)} has no range box (not a part or assembly)."),
        },
        _ => throw new IptException($"no range box for {Describe(item)}; pass an occurrence, body, face, edge, part or assembly."),
    };
}
#endif
