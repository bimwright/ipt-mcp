#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Linq;
using Inventor;

namespace Bimwright.Ipt.Scripting;

/// <summary>Transient B-rep solids (mm) — build, combine and move them in memory, then add them to a part.</summary>
public static partial class Ipt
{
    private static TransientBRep BRep => App.TransientBRep;

    /// <summary>A solid box between two corners [x,y,z] mm (any two opposite corners).</summary>
    public static SurfaceBody Box(double[] cornerA, double[] cornerB)
    {
        if (cornerA is not { Length: 3 } || cornerB is not { Length: 3 }) throw new IptException("Box needs two corners [x,y,z] mm.");
        return Box(cornerA[0], cornerA[1], cornerA[2], cornerB[0], cornerB[1], cornerB[2]);
    }

    /// <summary>A solid box between two Inventor points, for example from <see cref="Pt"/> (any two opposite corners).</summary>
    public static SurfaceBody Box(Point cornerA, Point cornerB) =>
        Box(cornerA.X * 10, cornerA.Y * 10, cornerA.Z * 10, cornerB.X * 10, cornerB.Y * 10, cornerB.Z * 10);

    /// <summary>A solid box between (x0,y0,z0) and (x1,y1,z1) mm.</summary>
    public static SurfaceBody Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        if (Eq(x0, x1) || Eq(y0, y1) || Eq(z0, z1)) throw new IptException("Box has zero size along one axis.");
        var box = Tg.CreateBox();
        box.MinPoint = Pt(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1));
        box.MaxPoint = Pt(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));
        return BRep.CreateSolidBlock(box);
    }

    /// <summary>A solid cylinder from the centre of its base to the centre of its top [x,y,z] mm, radius mm.</summary>
    public static SurfaceBody Cyl(double[] baseMm, double[] topMm, double radius) => Cone(baseMm, topMm, radius, radius);

    /// <summary>A solid cone or frustum: base radius <paramref name="r0"/>, top radius <paramref name="r1"/> (0 = pointed), mm.</summary>
    public static SurfaceBody Cone(double[] baseMm, double[] topMm, double r0, double r1)
    {
        if (baseMm is not { Length: 3 } || topMm is not { Length: 3 }) throw new IptException("Cyl/Cone need base and top [x,y,z] mm.");
        if (r0 <= 0 || r1 < 0) throw new IptException("Cyl/Cone need a base radius > 0 and a top radius ≥ 0.");
        if (Same(baseMm, topMm)) throw new IptException("Cyl/Cone base and top are the same point.");
        return BRep.CreateSolidCylinderCone(Pt(baseMm[0], baseMm[1], baseMm[2]), Pt(topMm[0], topMm[1], topMm[2]), Cm(r0), Cm(r0), Cm(r1));
    }

    /// <summary>A solid sphere, centre [x,y,z] mm, radius mm.</summary>
    public static SurfaceBody Sphere(double[] centerMm, double radius)
    {
        if (centerMm is not { Length: 3 } || radius <= 0) throw new IptException("Sphere needs a centre [x,y,z] mm and a radius > 0.");
        return BRep.CreateSolidSphere(Pt(centerMm[0], centerMm[1], centerMm[2]), Cm(radius));
    }

    /// <summary>A new body: <paramref name="first"/> united with every other body. Inputs are not changed.</summary>
    public static SurfaceBody Union(SurfaceBody first, params SurfaceBody[] others) => Boolean(first, others, BooleanTypeEnum.kBooleanTypeUnion, "Union");

    /// <summary>A new body: <paramref name="blank"/> minus every tool body. Inputs are not changed.</summary>
    public static SurfaceBody Subtract(SurfaceBody blank, params SurfaceBody[] tools) => Boolean(blank, tools, BooleanTypeEnum.kBooleanTypeDifference, "Subtract");

    /// <summary>A new body: the common volume of two bodies. Inputs are not changed.</summary>
    public static SurfaceBody Intersect(SurfaceBody a, SurfaceBody b) => Boolean(a, new[] { b }, BooleanTypeEnum.kBooleanTypeIntersect, "Intersect");

    /// <summary>A moved copy of a body (dx, dy, dz mm).</summary>
    public static SurfaceBody Moved(SurfaceBody body, double dx, double dy, double dz)
    {
        var m = Tg.CreateMatrix();
        m.SetTranslation(Vec(dx, dy, dz));
        var copy = BRep.Copy(body);
        BRep.Transform(copy, m);
        return copy;
    }

    /// <summary>A copy of a body rotated by <paramref name="deg"/> degrees about the axis through <paramref name="axisPointMm"/> along <paramref name="axisDir"/>.</summary>
    public static SurfaceBody Rotated(SurfaceBody body, double[] axisPointMm, double[] axisDir, double deg)
    {
        if (axisPointMm is not { Length: 3 } || axisDir is not { Length: 3 }) throw new IptException("Rotated needs an axis point [x,y,z] mm and an axis direction [x,y,z].");
        var m = Tg.CreateMatrix();
        m.SetToRotation(Rad(deg), Vec(axisDir[0], axisDir[1], axisDir[2]), Pt(axisPointMm[0], axisPointMm[1], axisPointMm[2]));
        var copy = BRep.Copy(body);
        BRep.Transform(copy, m);
        return copy;
    }

    /// <summary>
    /// Adds a transient body to a part (name/path/Document, or null = active) as a non-parametric base feature and
    /// names the resulting body. Returns that body.
    /// </summary>
    public static SurfaceBody AddBody(object? part, SurfaceBody body, string? name = null)
    {
        var p = Part(part);
        var features = p.ComponentDefinition.Features.NonParametricBaseFeatures;
        var definition = features.CreateDefinition();
        definition.BRepEntities = Coll(body);
        definition.OutputType = BaseFeatureOutputTypeEnum.kSolidOutputType;
        NonParametricBaseFeature feature;
        try { feature = features.AddByDefinition(definition); }
        catch (Exception ex) { throw new IptException($"Could not add the body to '{Safe(() => p.DisplayName)}': {ex.Message}"); }
        SurfaceBody? added = null;
        foreach (SurfaceBody b in feature.SurfaceBodies) { added = b; break; }
        if (added == null) throw new IptException("The base feature was created but has no body.");
        if (!string.IsNullOrWhiteSpace(name))
        {
            try { added.Name = name; }
            catch (Exception ex) { throw new IptException($"Body added, but it could not be named '{name}': {ex.Message}"); }
        }
        return added;
    }

    /// <summary>
    /// Creates a new part from the default part template, adds the bodies (named body_1, body_2, … unless the bodies
    /// already carry names you pass via <see cref="AddBody"/> later), saves it to <paramref name="path"/> and returns it
    /// (left open, hidden). Never overwrites: an existing file is an error. The new file is outside the script's undo.
    /// </summary>
    public static PartDocument NewPart(string path, params SurfaceBody[] bodies)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase))
            throw new IptException("NewPart needs a full path ending in .ipt.");
        var full = path.Trim().Replace('/', '\\');
        if (System.IO.File.Exists(full)) throw new IptException($"'{full}' already exists; NewPart never overwrites. Choose a new file name.");
        var folder = System.IO.Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) throw new IptException($"Folder '{folder}' does not exist.");
        var template = App.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
        var part = (PartDocument)App.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, false);
        try
        {
            var i = 0;
            foreach (var b in bodies ?? Array.Empty<SurfaceBody>()) AddBody(part, b, "body_" + ++i);
            part.SaveAs(full, false);
            return part;
        }
        catch
        {
            try { part.Close(true); } catch { }
            throw;
        }
    }

    private static SurfaceBody Boolean(SurfaceBody first, SurfaceBody[] others, BooleanTypeEnum type, string name)
    {
        if (first is null) throw new IptException(name + " needs a first body.");
        var result = BRep.Copy(first);
        foreach (var tool in others ?? Array.Empty<SurfaceBody>())
        {
            try { BRep.DoBoolean(result, BRep.Copy(tool), type); }
            catch (Exception ex) { throw new IptException($"{name} failed: {ex.Message}"); }
        }
        return result;
    }
}
#endif
