#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Assembly;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Scripting;

public static partial class Ipt
{
    /// <summary>
    /// Occurrences of an assembly (name/path, AssemblyDocument, definition, or null = active) matching a selector — the
    /// same selector as inventor_list_occurrences: a name, an array of names, or an object such as
    /// <c>new { regex = "^VENT_", leaf = true, max_depth = 2, path_contains = "FRAME", file = "*.ipt", include_suppressed = true }</c>.
    /// null = every occurrence. Nested ones are assembly-context proxies. Throws on no match (with the closest names)
    /// unless <paramref name="allowEmpty"/>.
    /// </summary>
    public static List<ComponentOccurrence> Occs(object? assembly = null, object? selector = null, bool allowEmpty = false)
    {
        var def = AsmDef(assembly);
        if (!OccurrenceSelection.TryResolve(def, Selector(selector), "selector", out var items, out var error,
                OccurrenceSelectorSpec.MaxLimit, allowEmpty))
            throw new IptException(error ?? "occurrence selector failed");
        return items.Select(i => i.Occurrence).ToList();
    }

    /// <summary>Exactly one occurrence matching the selector; throws listing the matches when there are several.</summary>
    public static ComponentOccurrence Occ(object? assembly, object selector)
    {
        var def = AsmDef(assembly);
        if (!OccurrenceSelection.TryResolve(def, Selector(selector), "selector", out var items, out var error, OccurrenceSelectorSpec.MaxLimit))
            throw new IptException(error ?? "occurrence selector failed");
        if (items.Count > 1)
            throw new IptException($"{items.Count} occurrences match: {List(items.Select(i => i.Path))}. Narrow the selector (full path 'Sub:1/Part:2', regex, or path_contains).");
        return items[0].Occurrence;
    }

    /// <summary>Leaf occurrences (parts) of an assembly, depth-first. Suppressed ones are skipped.</summary>
    public static List<ComponentOccurrence> Leaves(object? assembly = null) =>
        Occs(assembly, new JObject { ["leaf"] = true }, allowEmpty: true);

    /// <summary>The occurrence origin in the top assembly, [x,y,z] mm.</summary>
    public static double[] OriginMm(ComponentOccurrence occurrence)
    {
        var m = occurrence.Transformation;
        return CleanMm(m.Cell[1, 4], m.Cell[2, 4], m.Cell[3, 4]);
    }

    /// <summary>Placement of an occurrence in the top assembly: origin in mm and the unit axes, cleaned.</summary>
    public sealed class Transform
    {
        public double[] OriginMm { get; set; } = new double[3];
        public double[] XAxis { get; set; } = new double[3];
        public double[] YAxis { get; set; } = new double[3];
        public double[] ZAxis { get; set; } = new double[3];
        public override string ToString() => $"origin [{string.Join(", ", OriginMm)}] x [{string.Join(", ", XAxis)}] y [{string.Join(", ", YAxis)}]";
    }

    /// <summary>The occurrence's origin (mm) and axes — the same values as inventor_list_occurrences field "transform".</summary>
    public static Transform TransformMm(ComponentOccurrence occurrence)
    {
        var m = occurrence.Transformation;
        double[] Axis(int c) => new[] { Clean(m.Cell[1, c]), Clean(m.Cell[2, c]), Clean(m.Cell[3, c]) };
        return new Transform { OriginMm = OriginMm(occurrence), XAxis = Axis(1), YAxis = Axis(2), ZAxis = Axis(3) };
    }

    /// <summary>Moves the occurrence so its origin is at (x, y, z) mm, keeping its rotation. Returns the origin read back.</summary>
    public static double[] MoveTo(ComponentOccurrence occurrence, double x, double y, double z)
    {
        var m = occurrence.Transformation;
        m.SetTranslation(Tg.CreateVector(Cm(x), Cm(y), Cm(z)));
        return Apply(occurrence, m, new[] { x, y, z });
    }

    /// <summary>Moves the occurrence by (dx, dy, dz) mm. Returns the origin read back.</summary>
    public static double[] MoveBy(ComponentOccurrence occurrence, double dx, double dy, double dz)
    {
        var m = occurrence.Transformation;
        var target = new[] { Mm(m.Cell[1, 4]) + dx, Mm(m.Cell[2, 4]) + dy, Mm(m.Cell[3, 4]) + dz };
        m.SetTranslation(Tg.CreateVector(Cm(target[0]), Cm(target[1]), Cm(target[2])));
        return Apply(occurrence, m, target);
    }

    /// <summary>
    /// Places the occurrence with origin <paramref name="originMm"/> and axes <paramref name="xAxis"/>/<paramref name="yAxis"/>
    /// (z = x × y; both are normalized, y is made perpendicular to x). Returns the origin read back.
    /// </summary>
    public static double[] SetTransform(ComponentOccurrence occurrence, double[] originMm, double[] xAxis, double[] yAxis)
    {
        if (originMm is not { Length: 3 } || xAxis is not { Length: 3 } || yAxis is not { Length: 3 })
            throw new IptException("SetTransform needs origin [x,y,z] mm and two axes [x,y,z].");
        var x = Normalize(xAxis);
        var dot = x[0] * yAxis[0] + x[1] * yAxis[1] + x[2] * yAxis[2];
        var y = Normalize(new[] { yAxis[0] - dot * x[0], yAxis[1] - dot * x[1], yAxis[2] - dot * x[2] });
        var z = new[] { x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0] };
        var m = Tg.CreateMatrix();
        m.SetCoordinateSystem(Tg.CreatePoint(Cm(originMm[0]), Cm(originMm[1]), Cm(originMm[2])),
            Tg.CreateVector(x[0], x[1], x[2]), Tg.CreateVector(y[0], y[1], y[2]), Tg.CreateVector(z[0], z[1], z[2]));
        return Apply(occurrence, m, originMm);
    }

    /// <summary>
    /// Pairs (a, b) whose range boxes overlap after growing each by <paramref name="gapMm"/> — a fast pre-filter so a
    /// script checks only the candidate pairs instead of every pair. Items: occurrences, bodies, faces or edges. When
    /// both lists are the same list, each unordered pair is returned once and an item is never paired with itself.
    /// </summary>
    public static List<(T A, U B)> Overlapping<T, U>(IEnumerable<T> a, IEnumerable<U> b, double gapMm = 0)
    {
        var left = a.Select(x => (Item: x, Box: RangeBoxOf(x!))).ToList();
        var same = ReferenceEquals(a, b);
        var right = same ? left.Select(x => ((U)(object)x.Item!, x.Box)).ToList() : b.Select(x => (Item: x, Box: RangeBoxOf(x!))).ToList();
        var gapCm = Cm(gapMm);
        var pairs = new List<(T, U)>();
        for (var i = 0; i < left.Count; i++)
        {
            IptGuard.Check("Ipt.Overlapping", 0);
            for (var j = same ? i + 1 : 0; j < right.Count; j++)
                if (OccurrenceSelection.Overlap(left[i].Box, right[j].Item2, gapCm)) pairs.Add((left[i].Item, right[j].Item1));
        }
        return pairs;
    }

    // ---- internals ---------------------------------------------------------------------------------------------

    internal static AssemblyComponentDefinition AsmDef(object? assembly) => assembly switch
    {
        AssemblyComponentDefinition def => def,
        _ => Asm(assembly).ComponentDefinition,
    };

    internal static JToken? Selector(object? selector) => selector switch
    {
        null => null,
        string s => new JValue(s),
        IEnumerable<string> names => new JArray(names),
        _ => Json(selector),
    };

    private static double[] Apply(ComponentOccurrence occurrence, Matrix m, double[] requestedMm)
    {
        try { occurrence.Transformation = m; }
        catch (Exception ex) { throw new IptException($"Could not move occurrence '{Safe(() => occurrence.Name)}': {ex.Message}"); }
        var actual = OriginMm(occurrence);
        if (!Same(actual, requestedMm, 1e-4))
            throw new IptException($"Occurrence '{Safe(() => occurrence.Name)}' stayed at [{string.Join(", ", actual)}] mm instead of " +
                                   $"[{string.Join(", ", requestedMm.Select(v => Clean(v)))}]; constraints hold its position. " +
                                   "Suppress or delete those constraints first (inventor_list_constraints).");
        return actual;
    }

    private static double[] Normalize(double[] v)
    {
        var l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (l < 1e-12) throw new IptException("axis vectors must be non-zero and not parallel.");
        return new[] { v[0] / l, v[1] / l, v[2] / l };
    }
}
#endif
