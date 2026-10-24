using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// <c>build_solids</c> items, parsed and validated host-free (lengths mm). Every item is one extrude
/// feature in a part: <c>{name, op?: new|join|cut, target?, &lt;shape&gt;}</c> with exactly one shape
/// (name is optional for join/cut: <c>&lt;op&gt;_&lt;index&gt;</c>; shape keys may come from <c>defaults</c>)
/// <code>
/// box:     { min:[x,y,z], max:[x,y,z] }
/// tube:    { a:[x,y,z], b:[x,y,z], od, wall? }            (wall omitted/0 = solid rod)
/// prism:   { axis: x|y|z, from, to, profile }             (profile coords: x→(y,z), y→(x,z), z→(x,y))
/// extrude: { plane: {origin_mm, x_axis, y_axis}, depth, profile }   (any PoseSpec form; depth along x_axis × y_axis, may be negative)
/// </code>
/// <c>profile</c> uses the <see cref="PartRecipe"/> shape syntax (rect | circle | polyline with bulge, inner).
/// Each item is normalised to a sketch frame (origin, u, v), a signed depth along u × v and a profile
/// in (u, v) mm. Validation reports every error at once with its item index — degenerate sizes,
/// self-intersecting or touching loops, holes outside their outer loop, duplicate names and targets
/// that are not bodies created earlier — so a profile that Inventor could not build is rejected
/// before the host is touched.
/// </summary>
public sealed class SolidSpec
{
    public const int MaxItems = 1000;
    public const int MaxErrors = 50;
    private const double Eps = 1e-6;

    public sealed class Item
    {
        public int Index { get; internal set; }
        public string Name { get; internal set; } = "";
        public string Op { get; internal set; } = "new";
        public string? Target { get; internal set; }
        public string Kind { get; internal set; } = "";
        public double[] OriginMm { get; internal set; } = new double[3];
        public double[] U { get; internal set; } = { 1, 0, 0 };
        public double[] V { get; internal set; } = { 0, 1, 0 };
        public double DepthMm { get; internal set; }
        public List<PartRecipe.Shape> Profile { get; internal set; } = new();
    }

    public List<Item> Items { get; } = new();

    private static readonly string[] Kinds = { "box", "tube", "prism", "extrude" };

    /// <param name="defaults">Optional <c>{box|tube|prism|extrude: {key: value}}</c>: keys an item's
    /// shape leaves out are taken from the entry for its kind (e.g. <c>{tube: {od: 48.3}}</c>).</param>
    public static bool TryParse(JToken? token, out SolidSpec spec, out JArray errors, JToken? defaults = null)
    {
        spec = new SolidSpec();
        errors = new JArray();
        if (token is not JArray items || items.Count == 0)
        {
            errors.Add(Error(-1, null, "items", "must be a non-empty array of {name, op?, target?, box|tube|prism|extrude}"));
            return false;
        }
        if (items.Count > MaxItems)
        {
            errors.Add(Error(-1, null, "items", $"at most {MaxItems} items per call; split the build and continue in the next call"));
            return false;
        }
        if (defaults is { Type: not JTokenType.Null })
        {
            if (defaults is not JObject d)
                errors.Add(Error(-1, null, "defaults", "must be an object {box|tube|prism|extrude: {key: value}}"));
            else
                foreach (var p in d.Properties())
                    if (!Kinds.Contains(p.Name) || p.Value is not JObject)
                        errors.Add(Error(-1, null, "defaults." + p.Name, "must be a shape kind (box, tube, prism, extrude) holding an object of default keys"));
            if (errors.Count > 0) return false;
        }

        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var newBodies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            var path = $"items[{i}]";
            if (items[i] is not JObject o) { Add(errors, Error(i, null, path, "must be an object")); continue; }
            var op = o["op"]?.Type == JTokenType.String ? ((string)o["op"]!).Trim().ToLowerInvariant() : "new";
            var name = (o["name"]?.Type == JTokenType.String ? ((string)o["name"]!).Trim() : "");
            // join/cut items create no body, so their name (which only names the feature) is optional
            if (name.Length == 0 && op is "join" or "cut") name = $"{op}_{i}";
            var before = errors.Count;
            if (name.Length == 0) Add(errors, Error(i, null, path + ".name", "is required for op=new (it names the body)"));
            else if (names.TryGetValue(name, out var dup)) Add(errors, Error(i, name, path + ".name", $"duplicates items[{dup}]"));
            else names[name] = i;

            var item = new Item { Index = i, Name = name, Op = op };
            if (item.Op is not ("new" or "join" or "cut")) Add(errors, Error(i, name, path + ".op", "must be new, join or cut"));
            item.Target = o["target"]?.Type == JTokenType.String ? ((string)o["target"]!).Trim() : null;
            if (item.Op == "new" && item.Target != null) Add(errors, Error(i, name, path + ".target", "only join and cut take a target"));
            if (item.Op is "join" or "cut")
            {
                if (string.IsNullOrEmpty(item.Target)) Add(errors, Error(i, name, path + ".target", $"is required for op={item.Op} (a body name)"));
                else if (names.TryGetValue(item.Target!, out var t) && t < i && !newBodies.ContainsKey(item.Target!))
                    Add(errors, Error(i, name, path + ".target", $"'{item.Target}' is a {(string?)items[t]?["op"]} item, not a body; target the body it changed"));
                else if (!newBodies.ContainsKey(item.Target!) && LaterNewIndex(items, i, item.Target!) is { } later)
                    Add(errors, Error(i, name, path + ".target", $"body '{item.Target}' is created later by items[{later}]; order the items so the body exists first"));
            }

            var kinds = Kinds.Where(k => o[k] is { Type: not JTokenType.Null }).ToArray();
            foreach (var p in o.Properties())
                if (p.Name is not ("name" or "op" or "target" or "box" or "tube" or "prism" or "extrude"))
                    Add(errors, Error(i, name, $"{path}.{p.Name}", "unknown key (allowed: name, op, target, box, tube, prism, extrude)"));
            if (kinds.Length != 1)
                Add(errors, Error(i, name, path, "needs exactly one shape: box, tube, prism or extrude"));
            else
            {
                item.Kind = kinds[0];
                var shapePath = $"{path}.{item.Kind}";
                var shape = o[item.Kind];
                if (shape is JObject own && defaults?[item.Kind] is JObject fallback)
                {
                    var merged = (JObject)own.DeepClone();
                    foreach (var p in fallback.Properties())
                        if (merged[p.Name] is null) merged[p.Name] = p.Value.DeepClone();
                    shape = merged;
                }
                var message = item.Kind switch
                {
                    "box" => ParseBox(shape, item, shapePath),
                    "tube" => ParseTube(shape, item, shapePath),
                    "prism" => ParsePrism(shape, item, shapePath),
                    _ => ParseExtrude(shape, item, shapePath),
                };
                if (message != null) Add(errors, Error(i, name, message.Value.path, message.Value.text));
                else foreach (var (p, text) in ProfileErrors(item.Profile, shapePath + ".profile")) Add(errors, Error(i, name, p, text));
            }
            if (item.Op == "new" && name.Length > 0 && !newBodies.ContainsKey(name)) newBodies[name] = i;
            if (errors.Count == before) spec.Items.Add(item);
            if (errors.Count >= MaxErrors) break;
        }
        return errors.Count == 0;
    }

    private static int? LaterNewIndex(JArray items, int from, string target)
    {
        for (var j = from + 1; j < items.Count; j++)
            if (items[j] is JObject o && string.Equals((string?)o["name"], target, StringComparison.OrdinalIgnoreCase)
                && ((string?)o["op"] ?? "new").Equals("new", StringComparison.OrdinalIgnoreCase))
                return j;
        return null;
    }

    // ---- shapes → (origin, u, v, depth, profile) ----

    private static (string path, string text)? ParseBox(JToken? t, Item item, string path)
    {
        if (t is not JObject o) return (path, "must be {min:[x,y,z], max:[x,y,z]}");
        if (!Vec3(o["min"], out var min)) return (path + ".min", "must be [x, y, z] in mm");
        if (!Vec3(o["max"], out var max)) return (path + ".max", "must be [x, y, z] in mm");
        for (var k = 0; k < 3; k++)
            if (max[k] - min[k] < Eps) return (path, $"max must exceed min on {"xyz"[k]} (got {min[k]}..{max[k]})");
        item.OriginMm = new[] { 0, 0, min[2] };
        item.U = new double[] { 1, 0, 0 };
        item.V = new double[] { 0, 1, 0 };
        item.DepthMm = max[2] - min[2];
        item.Profile = new List<PartRecipe.Shape> { new PartRecipe.Rect { X1 = min[0], Y1 = min[1], X2 = max[0], Y2 = max[1] } };
        return null;
    }

    private static (string path, string text)? ParseTube(JToken? t, Item item, string path)
    {
        if (t is not JObject o) return (path, "must be {a:[x,y,z], b:[x,y,z], od, wall?}");
        if (!Vec3(o["a"], out var a)) return (path + ".a", "must be [x, y, z] in mm");
        if (!Vec3(o["b"], out var b)) return (path + ".b", "must be [x, y, z] in mm");
        if (!Num(o["od"], out var od) || od <= Eps) return (path + ".od", "must be a number > 0 (mm)");
        var wall = 0.0;
        if (o["wall"] is { Type: not JTokenType.Null } w && (!Num(w, out wall) || wall < 0)) return (path + ".wall", "must be a number >= 0 (mm)");
        if (wall >= od / 2 - Eps) return (path + ".wall", $"must be less than od/2 ({od / 2}); omit it for a solid rod");
        var n = Sub(b, a);
        var length = Norm(n);
        if (length < Eps) return (path, "a and b must differ");
        var nh = Scale(n, 1 / length);
        var reference = Math.Abs(nh[2]) < 0.9 ? new double[] { 0, 0, 1 } : new double[] { 1, 0, 0 };
        var u = Unit(Cross(reference, nh));
        item.OriginMm = a;
        item.U = u;
        item.V = Cross(nh, u); // u × v = n̂, so a positive depth runs from a to b
        item.DepthMm = length;
        var outer = new PartRecipe.Circle { Cx = 0, Cy = 0, R = od / 2 };
        if (wall > 0) outer.Inner.Add(new PartRecipe.Circle { Cx = 0, Cy = 0, R = od / 2 - wall });
        item.Profile = new List<PartRecipe.Shape> { outer };
        return null;
    }

    private static (string path, string text)? ParsePrism(JToken? t, Item item, string path)
    {
        if (t is not JObject o) return (path, "must be {axis: x|y|z, from, to, profile}");
        var axis = o["axis"]?.Type == JTokenType.String ? ((string)o["axis"]!).Trim().ToLowerInvariant() : "";
        if (axis is not ("x" or "y" or "z")) return (path + ".axis", "must be x, y or z");
        if (!Num(o["from"], out var from)) return (path + ".from", "must be a number (mm)");
        if (!Num(o["to"], out var to)) return (path + ".to", "must be a number (mm)");
        if (Math.Abs(to - from) < Eps) return (path, "from and to must differ");
        if (!PartRecipe.TryParseProfile(o["profile"], path + ".profile", out var shapes, out var error))
            return Split(error!);
        // Profile coordinates are the other two axes in order; depth is signed along u × v.
        (item.OriginMm, item.U, item.V, item.DepthMm) = axis switch
        {
            "x" => (new[] { from, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 }, to - from),
            "y" => (new[] { 0, from, 0 }, new double[] { 1, 0, 0 }, new double[] { 0, 0, 1 }, -(to - from)),
            _ => (new[] { 0, 0, from }, new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, to - from),
        };
        item.Profile = shapes;
        return null;
    }

    private static (string path, string text)? ParseExtrude(JToken? t, Item item, string path)
    {
        if (t is not JObject o) return (path, "must be {plane: {origin_mm, x_axis, y_axis}, depth, profile}");
        if (o["plane"] is not JObject plane)
            return (path + ".plane", "must be {origin_mm:[x,y,z], x_axis:[..], y_axis:[..]}");
        if (!PoseSpec.TryParse(plane, "plane", out var pose, out var poseError))
            return (path + ".plane", poseError!);
        if (!Num(o["depth"], out var depth) || Math.Abs(depth) < Eps) return (path + ".depth", "must be a non-zero number (mm, along x_axis cross y_axis)");
        if (!PartRecipe.TryParseProfile(o["profile"], path + ".profile", out var shapes, out var error))
            return Split(error!);
        item.OriginMm = pose.OriginCm.Select(c => c * 10).ToArray();
        item.U = pose.X;
        item.V = pose.Y;
        item.DepthMm = depth;
        item.Profile = shapes;
        return null;
    }

    // ---- profile geometry: loops as polygons ----

    private static IEnumerable<(string path, string text)> ProfileErrors(List<PartRecipe.Shape> shapes, string path)
    {
        var loops = shapes.Select((s, i) => (shape: s, poly: Polygon(s), path: shapes.Count > 1 ? $"{path}[{i}]" : path)).ToList();
        foreach (var e in LoopSetErrors(loops, "loops in one profile")) yield return e;
    }

    private static IEnumerable<(string path, string text)> LoopSetErrors(List<(PartRecipe.Shape shape, List<(double x, double y)> poly, string path)> loops, string what)
    {
        foreach (var l in loops)
        {
            if (l.shape is PartRecipe.Polyline && SelfIntersects(l.poly))
                yield return (l.path, "polyline crosses itself; Inventor cannot build a profile from it");
            var inner = l.shape.Inner.Select((s, i) => (shape: s, poly: Polygon(s), path: $"{l.path}.inner[{i}]")).ToList();
            foreach (var h in inner)
                if (!Inside(h.poly, l.poly))
                    yield return (h.path, "hole must lie strictly inside its outer loop (no touching or crossing)");
            foreach (var e in LoopSetErrors(inner, "holes of one loop")) yield return e;
        }
        for (var i = 0; i < loops.Count; i++)
            for (var j = i + 1; j < loops.Count; j++)
                if (Crosses(loops[i].poly, loops[j].poly) || Inside(loops[j].poly, loops[i].poly) || Inside(loops[i].poly, loops[j].poly))
                    yield return (loops[j].path, $"{what} must not touch, cross or contain each other ({loops[i].path}); use inner for holes or a separate item");
    }

    private static List<(double x, double y)> Polygon(PartRecipe.Shape s)
    {
        switch (s)
        {
            case PartRecipe.Rect r:
                return new() { (r.X1, r.Y1), (r.X2, r.Y1), (r.X2, r.Y2), (r.X1, r.Y2) };
            case PartRecipe.Circle c:
                return Enumerable.Range(0, 72).Select(k => (c.Cx + c.R * Math.Cos(k * Math.PI / 36), c.Cy + c.R * Math.Sin(k * Math.PI / 36))).ToList();
            case PartRecipe.Polyline pl:
            {
                var pts = new List<(double x, double y)>();
                for (var i = 0; i < pl.Points.Count; i++)
                {
                    var a = pl.Points[i];
                    var b = pl.Points[(i + 1) % pl.Points.Count];
                    pts.Add((a.X, a.Y));
                    if (Math.Abs(a.Bulge) < 1e-12) continue;
                    // DXF bulge = tan(sweep/4), positive = counter-clockwise a→b: sample the arc.
                    var sweep = 4 * Math.Atan(a.Bulge);
                    double dx = b.X - a.X, dy = b.Y - a.Y, chord = Math.Sqrt(dx * dx + dy * dy);
                    var radius = chord / (2 * Math.Sin(Math.Abs(sweep) / 2));
                    double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, nx = -dy / chord, ny = dx / chord;
                    var offset = chord / 2 * (1 - a.Bulge * a.Bulge) / (2 * a.Bulge);
                    double cx = mx + nx * offset, cy = my + ny * offset;
                    var start = Math.Atan2(a.Y - cy, a.X - cx);
                    for (var k = 1; k < 8; k++)
                    {
                        var ang = start + sweep * k / 8;
                        pts.Add((cx + radius * Math.Cos(ang), cy + radius * Math.Sin(ang)));
                    }
                }
                return pts;
            }
        }
        return new();
    }

    private static bool SelfIntersects(List<(double x, double y)> p)
    {
        var n = p.Count;
        for (var i = 0; i < n; i++)
            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue; // neighbours through the closing edge
                if (SegmentsTouch(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n])) return true;
            }
        return false;
    }

    /// <summary>True when any edge of <paramref name="a"/> touches or crosses any edge of <paramref name="b"/>.</summary>
    private static bool Crosses(List<(double x, double y)> a, List<(double x, double y)> b)
    {
        if (!BoxesOverlap(a, b)) return false;
        for (var i = 0; i < a.Count; i++)
            for (var j = 0; j < b.Count; j++)
                if (SegmentsTouch(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
        return false;
    }

    /// <summary>Every vertex of <paramref name="inner"/> inside <paramref name="outer"/> and no edges touching.</summary>
    private static bool Inside(List<(double x, double y)> inner, List<(double x, double y)> outer)
        => inner.All(pt => PointInPolygon(pt, outer)) && !Crosses(inner, outer);

    private static bool BoxesOverlap(List<(double x, double y)> a, List<(double x, double y)> b)
        => a.Max(p => p.x) >= b.Min(p => p.x) - Eps && b.Max(p => p.x) >= a.Min(p => p.x) - Eps
        && a.Max(p => p.y) >= b.Min(p => p.y) - Eps && b.Max(p => p.y) >= a.Min(p => p.y) - Eps;

    private static bool PointInPolygon((double x, double y) pt, List<(double x, double y)> poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            if ((yi > pt.y) != (yj > pt.y) && pt.x < (xj - xi) * (pt.y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsTouch((double x, double y) p1, (double x, double y) p2, (double x, double y) q1, (double x, double y) q2)
    {
        double d1 = Orient(q1, q2, p1), d2 = Orient(q1, q2, p2), d3 = Orient(p1, p2, q1), d4 = Orient(p1, p2, q2);
        if (((d1 > Eps && d2 < -Eps) || (d1 < -Eps && d2 > Eps)) && ((d3 > Eps && d4 < -Eps) || (d3 < -Eps && d4 > Eps))) return true;
        return (Math.Abs(d1) <= Eps && OnSegment(q1, q2, p1)) || (Math.Abs(d2) <= Eps && OnSegment(q1, q2, p2))
            || (Math.Abs(d3) <= Eps && OnSegment(p1, p2, q1)) || (Math.Abs(d4) <= Eps && OnSegment(p1, p2, q2));
    }

    private static double Orient((double x, double y) a, (double x, double y) b, (double x, double y) c)
        => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    private static bool OnSegment((double x, double y) a, (double x, double y) b, (double x, double y) p)
        => p.x >= Math.Min(a.x, b.x) - Eps && p.x <= Math.Max(a.x, b.x) + Eps && p.y >= Math.Min(a.y, b.y) - Eps && p.y <= Math.Max(a.y, b.y) + Eps;

    // ---- small helpers ----

    private static JObject Error(int index, string? name, string path, string message)
    {
        var e = new JObject { ["path"] = path, ["message"] = message };
        if (index >= 0) e["index"] = index;
        if (!string.IsNullOrEmpty(name)) e["name"] = name;
        return e;
    }

    private static void Add(JArray errors, JObject error)
    {
        if (errors.Count < MaxErrors) errors.Add(error);
    }

    /// <summary>Splits a <see cref="PartRecipe"/> "path: message" error back into its parts.</summary>
    private static (string path, string text) Split(string error)
    {
        var at = error.IndexOf(": ", StringComparison.Ordinal);
        return at < 0 ? ("", error) : (error.Substring(0, at), error.Substring(at + 2));
    }

    private static bool Num(JToken? t, out double v)
    {
        v = 0;
        if (t is null || t.Type is not (JTokenType.Integer or JTokenType.Float)) return false;
        v = (double)t;
        return !double.IsNaN(v) && !double.IsInfinity(v);
    }

    private static bool Vec3(JToken? t, out double[] v)
    {
        v = new double[3];
        if (t is not JArray a || a.Count != 3) return false;
        for (var k = 0; k < 3; k++) if (!Num(a[k], out v[k])) return false;
        return true;
    }

    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double[] Scale(double[] a, double s) => new[] { a[0] * s, a[1] * s, a[2] * s };
    private static double Norm(double[] a) => Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
    private static double[] Unit(double[] a) => Scale(a, 1 / Norm(a));
    private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
}
