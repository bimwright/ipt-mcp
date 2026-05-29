#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Inventor.Shared.Handlers.Sketch;

/// <summary>
/// Shared sketch-handler helpers: resolving the "target" sketch and building geometry points.
/// </summary>
internal static class SketchSupport
{
    /// <summary>
    /// Resolve the sketch a draw/dimension/constraint command should act on. If <paramref name="name"/>
    /// is supplied it must match an existing sketch by name; otherwise the most recently created sketch
    /// (last in the collection) is used, which mirrors the "active sketch" an agent just created.
    /// Inventor exposes no typed <c>ActiveEditObject</c>/<c>ActiveSketch</c> accessor, so the
    /// last-sketch convention is the reliable substitute.
    /// </summary>
    public static PlanarSketch ResolveTargetSketch(PartComponentDefinition def, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var found = EntityResolver.FindSketch(def, name!);
            if (found is null) throw new ArgumentException($"no sketch named '{name}'");
            return found;
        }
        var sketches = def.Sketches;
        if (sketches.Count < 1) throw new ArgumentException("there is no sketch to draw into; create one first");
        return sketches[sketches.Count];
    }

    public static Point2d Pt(Application app, double xMm, double yMm) =>
        app.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(xMm), UnitConvert.MmToCm(yMm));
}
#endif
