using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// <c>create_part</c> recipe v1 (E2), parsed and validated host-free so every error names its
/// JSON path (<c>features[2].extrude.distance</c>) before Inventor is touched. Lengths are mm.
///
/// <code>
/// { template?, save_as?, overwrite?, close_after?, material?, iproperties?: {"Part Number": "…" | "Set:Prop": "…"},
///   parameters?: [{name, expression, unit?}],
///   features: [
///     { sketch:  { name?, plane: "XY"|"XZ"|"YZ"|"&lt;work plane&gt;" | {origin_mm, x_axis, y_axis}, profile: shape | [shape…] } },
///     { extrude: { sketch?, distance: mm | "expr", direction?: positive|negative|symmetric, operation?: join|cut|intersect|new_body, name? } },
///     { hole:    { face: {normal, extreme?, near_mm?}, at: [[x,y,z]…], diameter, through? | depth?, kind? } },
///     { fillet:  { radius, edges } },  { chamfer: { distance, edges: [ids] } } ] }
/// shape = { rect: {w, h, center?} | {from:[x,y], to:[x,y]} } | { circle: {d, center?} }
///       | { polyline: [[x,y] | [x,y,bulge]…] }        (closed; bulge = tan(sweep/4), + = CCW)
///       | any shape + inner: [shape…]                  (voids inside it)
/// </code>
/// Out of scope for v1 (use send_code + modules): revolve/loft/sweep, patterns, sheet metal, miters.
/// </summary>
public sealed class PartRecipe
{
    public const int MaxFeatures = 60;
    public const int MaxPolylinePoints = 500;

    public string? Template { get; private set; }
    public string? SaveAs { get; private set; }
    public bool Overwrite { get; private set; }
    public bool CloseAfter { get; private set; }
    public string? Material { get; private set; }
    public List<(string Set, string Prop, string Value)> IProperties { get; } = new();
    public List<(string Name, string Expression, string Unit)> Parameters { get; } = new();
    public List<Feature> Features { get; } = new();

    public abstract class Feature
    {
        public string Path { get; internal set; } = "";
        public abstract string Kind { get; }
    }

    public sealed class SketchFeature : Feature
    {
        public override string Kind => "sketch";
        public string? Name { get; internal set; }
        public string? PlaneName { get; internal set; }
        public PoseSpec? PlanePose { get; internal set; }
        public List<Shape> Shapes { get; } = new();
    }

    public sealed class ExtrudeFeature : Feature
    {
        public override string Kind => "extrude";
        public string? Sketch { get; internal set; }
        public JToken Distance { get; internal set; } = JValue.CreateNull();
        public string Direction { get; internal set; } = "positive";
        public string Operation { get; internal set; } = "join";
        public string? Name { get; internal set; }
    }

    /// <summary>hole/fillet/chamfer pass through to the existing wire handlers with these params.</summary>
    public sealed class PassThroughFeature : Feature
    {
        private readonly string _kind;
        public PassThroughFeature(string kind) => _kind = kind;
        public override string Kind => _kind;
        public string Command { get; internal set; } = "";
        public JObject Params { get; internal set; } = new();
    }

    public abstract class Shape
    {
        public List<Shape> Inner { get; } = new();
    }

    public sealed class Rect : Shape { public double X1, Y1, X2, Y2; }
    public sealed class Circle : Shape { public double Cx, Cy, R; }
    public sealed class Polyline : Shape { public List<(double X, double Y, double Bulge)> Points { get; } = new(); }

    public static bool TryParse(JToken? token, out PartRecipe recipe, out string? error)
    {
        recipe = new PartRecipe();
        error = null;
        try
        {
            recipe.Parse(token);
            return true;
        }
        catch (RecipeException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed class RecipeException : Exception
    {
        public RecipeException(string path, string message) : base(path + ": " + message) { }
    }

    private void Parse(JToken? token)
    {
        if (token is not JObject r) throw new RecipeException("recipe", "must be an object with features[]");
        Known(r, "recipe", "template", "save_as", "overwrite", "close_after", "material", "iproperties", "parameters", "features");
        Template = OptString(r, "template", "recipe");
        SaveAs = OptString(r, "save_as", "recipe");
        if (SaveAs != null && !SaveAs.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase))
            throw new RecipeException("recipe.save_as", "must be a full path ending in .ipt");
        Overwrite = OptBool(r, "overwrite", "recipe") ?? false;
        CloseAfter = OptBool(r, "close_after", "recipe") ?? false;
        if (CloseAfter && SaveAs is null) throw new RecipeException("recipe.close_after", "needs save_as (closing an unsaved part would discard it)");
        Material = OptString(r, "material", "recipe");

        if (r["iproperties"] is { Type: not JTokenType.Null } ip)
        {
            if (ip is not JObject ipo) throw new RecipeException("recipe.iproperties", "must be an object {\"Part Number\": \"…\"}");
            foreach (var p in ipo.Properties())
            {
                if (p.Value.Type is JTokenType.Object or JTokenType.Array or JTokenType.Null)
                    throw new RecipeException($"recipe.iproperties[\"{p.Name}\"]", "value must be a string or number");
                var colon = p.Name.IndexOf(':');
                var set = colon > 0 ? p.Name.Substring(0, colon) : DefaultSetFor(p.Name);
                var prop = colon > 0 ? p.Name.Substring(colon + 1) : p.Name;
                IProperties.Add((set, prop, Convert.ToString(((JValue)p.Value).Value, CultureInfo.InvariantCulture) ?? ""));
            }
        }

        if (r["parameters"] is { Type: not JTokenType.Null } pt)
        {
            if (pt is not JArray pa) throw new RecipeException("recipe.parameters", "must be an array of {name, expression, unit?}");
            for (var i = 0; i < pa.Count; i++)
            {
                var path = $"parameters[{i}]";
                if (pa[i] is not JObject po) throw new RecipeException(path, "must be an object {name, expression, unit?}");
                var name = ReqString(po, "name", path);
                var expr = po["expression"] is JValue { Type: JTokenType.Integer or JTokenType.Float } num
                    ? Convert.ToString(num.Value, CultureInfo.InvariantCulture)!
                    : ReqString(po, "expression", path);
                Parameters.Add((name, expr, OptString(po, "unit", path) ?? "mm"));
            }
        }

        if (r["features"] is not JArray fs || fs.Count == 0) throw new RecipeException("recipe.features", "must be a non-empty array");
        if (fs.Count > MaxFeatures) throw new RecipeException("recipe.features", $"at most {MaxFeatures} features (got {fs.Count})");
        string? lastSketch = null;
        var sketchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var autoSketch = 0;
        for (var i = 0; i < fs.Count; i++)
        {
            var path = $"features[{i}]";
            if (fs[i] is not JObject fo || fo.Count != 1)
                throw new RecipeException(path, "must be an object with exactly one key: sketch|extrude|hole|fillet|chamfer");
            var kind = fo.Properties().First().Name;
            var body = fo[kind] as JObject ?? throw new RecipeException($"{path}.{kind}", "must be an object");
            var fpath = $"{path}.{kind}";
            switch (kind)
            {
                case "sketch":
                {
                    var s = ParseSketch(body, fpath);
                    s.Name ??= "MCP_Sketch" + (++autoSketch);
                    if (!sketchNames.Add(s.Name)) throw new RecipeException(fpath + ".name", $"duplicate sketch name '{s.Name}'");
                    lastSketch = s.Name;
                    Features.Add(s);
                    break;
                }
                case "extrude":
                {
                    Known(body, fpath, "sketch", "distance", "direction", "operation", "name");
                    var e = new ExtrudeFeature { Path = fpath, Sketch = OptString(body, "sketch", fpath) ?? lastSketch };
                    if (e.Sketch is null) throw new RecipeException(fpath + ".sketch", "no sketch defined before this extrude");
                    if (!sketchNames.Contains(e.Sketch)) throw new RecipeException(fpath + ".sketch", $"unknown sketch '{e.Sketch}'");
                    var d = body["distance"];
                    if (d is null || d.Type == JTokenType.Null) throw new RecipeException(fpath + ".distance", "is required (mm number or expression string)");
                    if (d.Type is JTokenType.Integer or JTokenType.Float && (double)d <= 0) throw new RecipeException(fpath + ".distance", "must be > 0");
                    if (d.Type is not (JTokenType.Integer or JTokenType.Float or JTokenType.String)) throw new RecipeException(fpath + ".distance", "must be a number or string");
                    e.Distance = d;
                    e.Direction = OneOf(body, "direction", fpath, "positive", "positive", "negative", "symmetric");
                    e.Operation = OneOf(body, "operation", fpath, "join", "join", "cut", "intersect", "new_body");
                    e.Name = OptString(body, "name", fpath);
                    Features.Add(e);
                    break;
                }
                case "hole":
                {
                    Known(body, fpath, "face", "at", "diameter", "through", "depth", "kind", "name");
                    if (body["face"] is not JObject face) throw new RecipeException(fpath + ".face", "must be {normal: +X|-X|+Y|-Y|+Z|-Z, extreme?: max|min, near_mm?}");
                    var normal = ReqString(face, "normal", fpath + ".face");
                    if (!new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z" }.Contains(normal.ToUpperInvariant()))
                        throw new RecipeException(fpath + ".face.normal", "must be +X|-X|+Y|-Y|+Z|-Z");
                    if (body["at"] is not JArray at || at.Count == 0 || at.Any(t => t is not JArray { Count: 3 }))
                        throw new RecipeException(fpath + ".at", "must be [[x,y,z], …] in mm on the face plane");
                    var dia = ReqNumber(body, "diameter", fpath);
                    if (dia <= 0) throw new RecipeException(fpath + ".diameter", "must be > 0");
                    var through = OptBool(body, "through", fpath);
                    var depth = body["depth"] is { Type: not JTokenType.Null } ? ReqNumber(body, "depth", fpath) : (double?)null;
                    if ((through == true) == (depth != null)) throw new RecipeException(fpath, "give exactly one of through:true or depth");
                    var hp = new JObject
                    {
                        ["face"] = new JObject
                        {
                            ["kind"] = "planar",
                            ["normal"] = normal.ToUpperInvariant(),
                            ["extreme"] = OptString(face, "extreme", fpath + ".face") ?? "max",
                        },
                        ["points_mm"] = at.DeepClone(),
                        ["diameter_mm"] = dia,
                        ["kind"] = OptString(body, "kind", fpath) ?? "drilled",
                    };
                    if (face["near_mm"] is JArray near) ((JObject)hp["face"]!)["near_mm"] = near.DeepClone();
                    if (depth != null) { hp["through"] = false; hp["depth_mm"] = depth; } else hp["through"] = true;
                    Features.Add(new PassThroughFeature("hole") { Path = fpath, Command = "hole", Params = hp });
                    break;
                }
                case "fillet":
                {
                    Known(body, fpath, "radius", "edges");
                    var rad = ReqNumber(body, "radius", fpath);
                    if (rad <= 0) throw new RecipeException(fpath + ".radius", "must be > 0");
                    var edges = body["edges"] ?? throw new RecipeException(fpath + ".edges", "is required (edge ids or a selector object)");
                    Features.Add(new PassThroughFeature("fillet") { Path = fpath, Command = "fillet", Params = new JObject { ["radius_mm"] = rad, ["edges"] = edges.DeepClone() } });
                    break;
                }
                case "chamfer":
                {
                    Known(body, fpath, "distance", "edges");
                    var dist = ReqNumber(body, "distance", fpath);
                    if (dist <= 0) throw new RecipeException(fpath + ".distance", "must be > 0");
                    if (body["edges"] is not JArray ce || ce.Count == 0) throw new RecipeException(fpath + ".edges", "must be a non-empty array of edge ids");
                    Features.Add(new PassThroughFeature("chamfer") { Path = fpath, Command = "chamfer", Params = new JObject { ["edge_ids"] = ce.DeepClone(), ["distance_mm"] = dist } });
                    break;
                }
                default:
                    throw new RecipeException(path, $"unknown feature '{kind}' (v1: sketch|extrude|hole|fillet|chamfer; use send_code + modules for revolve/loft/sweep/patterns)");
            }
        }
        if (!Features.Any(f => f is ExtrudeFeature))
            throw new RecipeException("recipe.features", "needs at least one extrude (a part without a solid is not useful)");
    }

    private SketchFeature ParseSketch(JObject body, string path)
    {
        Known(body, path, "name", "plane", "profile");
        var s = new SketchFeature { Path = path, Name = OptString(body, "name", path) };
        var plane = body["plane"];
        if (plane is null || plane.Type == JTokenType.Null) s.PlaneName = "XY";
        else if (plane.Type == JTokenType.String) s.PlaneName = (string)plane!;
        else if (plane is JObject)
        {
            if (!PoseSpec.TryParse(plane, path + ".plane", out var pose, out var err)) throw new RecipeException(path + ".plane", err!.Substring(err.IndexOf(':') + 1).Trim());
            if (plane["origin_mm"] is null || plane["x_axis"] is null || plane["y_axis"] is null)
                throw new RecipeException(path + ".plane", "a fixed plane needs origin_mm, x_axis and y_axis");
            s.PlanePose = pose;
        }
        else throw new RecipeException(path + ".plane", "must be XY|XZ|YZ, a work plane name, or {origin_mm, x_axis, y_axis}");

        var prof = body["profile"] ?? throw new RecipeException(path + ".profile", "is required");
        var shapes = prof is JArray pa ? pa.ToList() : new List<JToken> { prof };
        if (shapes.Count == 0) throw new RecipeException(path + ".profile", "must contain at least one shape");
        for (var i = 0; i < shapes.Count; i++)
            s.Shapes.Add(ParseShape(shapes[i], prof is JArray ? $"{path}.profile[{i}]" : path + ".profile"));
        return s;
    }

    private static Shape ParseShape(JToken t, string path)
    {
        if (t is not JObject o) throw new RecipeException(path, "must be {rect|circle|polyline, inner?}");
        var kinds = o.Properties().Select(p => p.Name).Where(n => n != "inner").ToList();
        if (kinds.Count != 1) throw new RecipeException(path, "needs exactly one of rect|circle|polyline (plus optional inner)");
        Shape shape;
        var k = kinds[0];
        var kp = $"{path}.{k}";
        switch (k)
        {
            case "rect":
            {
                var rt = o["rect"];
                if (rt is JArray ra && ra.Count == 2 && TryNum(ra[0], out var w0) && TryNum(ra[1], out var h0))
                    rt = new JObject { ["w"] = w0, ["h"] = h0 };
                if (rt is not JObject ro) throw new RecipeException(kp, "must be {w, h, center?} or {from:[x,y], to:[x,y]} or [w, h]");
                if (ro["from"] != null || ro["to"] != null)
                {
                    var f = Pt(ro["from"], kp + ".from");
                    var to = Pt(ro["to"], kp + ".to");
                    if (Math.Abs(f.x - to.x) < 1e-9 || Math.Abs(f.y - to.y) < 1e-9) throw new RecipeException(kp, "rectangle has zero width or height");
                    shape = new Rect { X1 = f.x, Y1 = f.y, X2 = to.x, Y2 = to.y };
                }
                else
                {
                    var w = ReqNumber(ro, "w", kp);
                    var h = ReqNumber(ro, "h", kp);
                    if (w <= 0 || h <= 0) throw new RecipeException(kp, "w and h must be > 0");
                    var c = ro["center"] is { Type: not JTokenType.Null } ct ? Pt(ct, kp + ".center") : (0, 0);
                    shape = new Rect { X1 = c.x - w / 2, Y1 = c.y - h / 2, X2 = c.x + w / 2, Y2 = c.y + h / 2 };
                }
                break;
            }
            case "circle":
            {
                if (o["circle"] is not JObject co) throw new RecipeException(kp, "must be {d, center?} (or r)");
                double r;
                if (co["d"] is { Type: not JTokenType.Null }) r = ReqNumber(co, "d", kp) / 2;
                else r = ReqNumber(co, "r", kp);
                if (r <= 0) throw new RecipeException(kp, "diameter must be > 0");
                var c = co["center"] is { Type: not JTokenType.Null } ct ? Pt(ct, kp + ".center") : (0, 0);
                shape = new Circle { Cx = c.x, Cy = c.y, R = r };
                break;
            }
            case "polyline":
            {
                if (o["polyline"] is not JArray pts || pts.Count < 3) throw new RecipeException(kp, "must hold at least 3 points [x,y] or [x,y,bulge]");
                if (pts.Count > MaxPolylinePoints) throw new RecipeException(kp, $"at most {MaxPolylinePoints} points");
                var pl = new Polyline();
                for (var i = 0; i < pts.Count; i++)
                {
                    if (pts[i] is not JArray pa || pa.Count is < 2 or > 3 || !TryNum(pa[0], out var x) || !TryNum(pa[1], out var y))
                        throw new RecipeException($"{kp}[{i}]", "must be [x, y] or [x, y, bulge]");
                    var bulge = 0.0;
                    if (pa.Count == 3 && !TryNum(pa[2], out bulge)) throw new RecipeException($"{kp}[{i}]", "bulge must be a number");
                    pl.Points.Add((x, y, bulge));
                }
                // a repeated closing point is tolerated and dropped
                var first = pl.Points[0];
                var last = pl.Points[pl.Points.Count - 1];
                if (Math.Abs(first.X - last.X) < 1e-9 && Math.Abs(first.Y - last.Y) < 1e-9) pl.Points.RemoveAt(pl.Points.Count - 1);
                if (pl.Points.Count < 3 && pl.Points.All(p => p.Bulge == 0)) throw new RecipeException(kp, "needs at least 3 distinct points");
                for (var i = 0; i < pl.Points.Count; i++)
                {
                    var a = pl.Points[i];
                    var b = pl.Points[(i + 1) % pl.Points.Count];
                    if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9) throw new RecipeException($"{kp}[{i}]", "duplicate consecutive point");
                }
                shape = pl;
                break;
            }
            default:
                throw new RecipeException(path, $"unknown shape '{k}' (rect|circle|polyline)");
        }
        if (o["inner"] is { Type: not JTokenType.Null } inner)
        {
            if (inner is not JArray ia) throw new RecipeException(path + ".inner", "must be an array of shapes");
            for (var i = 0; i < ia.Count; i++) shape.Inner.Add(ParseShape(ia[i], $"{path}.inner[{i}]"));
        }
        return shape;
    }

    /// <summary>Well-known iProperties land in their set without a "Set:" prefix.</summary>
    private static string DefaultSetFor(string prop) => prop switch
    {
        "Title" or "Subject" or "Author" or "Keywords" or "Comments" => "Inventor Summary Information",
        "Company" or "Manager" or "Category" => "Inventor Document Summary Information",
        _ => "Design Tracking Properties",
    };

    private static (double x, double y) Pt(JToken? t, string path)
    {
        if (t is JArray a && a.Count == 2 && TryNum(a[0], out var x) && TryNum(a[1], out var y)) return (x, y);
        throw new RecipeException(path, "must be [x, y] in mm");
    }

    private static bool TryNum(JToken t, out double v)
    {
        v = 0;
        if (t.Type is not (JTokenType.Integer or JTokenType.Float)) return false;
        v = (double)t;
        return !double.IsNaN(v) && !double.IsInfinity(v);
    }

    private static void Known(JObject o, string path, params string[] keys)
    {
        foreach (var p in o.Properties())
            if (!keys.Contains(p.Name)) throw new RecipeException(path, $"unknown key '{p.Name}' (allowed: {string.Join(", ", keys)})");
    }

    private static string? OptString(JObject o, string key, string path)
    {
        var t = o[key];
        if (t is null || t.Type == JTokenType.Null) return null;
        if (t.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)t)) throw new RecipeException($"{path}.{key}", "must be a non-empty string");
        return ((string)t!).Trim();
    }

    private static string ReqString(JObject o, string key, string path)
        => OptString(o, key, path) ?? throw new RecipeException($"{path}.{key}", "is required");

    private static bool? OptBool(JObject o, string key, string path)
    {
        var t = o[key];
        if (t is null || t.Type == JTokenType.Null) return null;
        if (t.Type != JTokenType.Boolean) throw new RecipeException($"{path}.{key}", "must be a boolean");
        return (bool)t;
    }

    private static double ReqNumber(JObject o, string key, string path)
    {
        var t = o[key];
        if (t is null || !TryNum(t, out var v)) throw new RecipeException($"{path}.{key}", "must be a number (mm)");
        return v;
    }

    private static string OneOf(JObject o, string key, string path, string dflt, params string[] allowed)
    {
        var v = OptString(o, key, path) ?? dflt;
        if (!allowed.Contains(v)) throw new RecipeException($"{path}.{key}", "must be " + string.Join("|", allowed));
        return v;
    }

    /// <summary>Human-readable step list for dry_run.</summary>
    public JArray Plan()
    {
        var steps = new JArray { "new_part" + (Template != null ? " (template " + Template + ")" : "") };
        foreach (var p in Parameters) steps.Add($"parameter {p.Name} = {p.Expression} [{p.Unit}]");
        foreach (var f in Features)
        {
            steps.Add(f switch
            {
                SketchFeature s => $"{s.Path}: sketch {s.Name} on {(s.PlanePose != null ? "fixed plane" : s.PlaneName)} with {s.Shapes.Count} shape(s), {s.Shapes.Sum(x => x.Inner.Count)} inner loop(s)",
                ExtrudeFeature e => $"{e.Path}: extrude {e.Sketch} {e.Distance} {e.Direction} {e.Operation}",
                PassThroughFeature pf => $"{pf.Path}: {pf.Command}",
                _ => f.Path,
            });
        }
        if (Material != null) steps.Add("material " + Material);
        foreach (var ip in IProperties) steps.Add($"iproperty {ip.Set}/{ip.Prop} = {ip.Value}");
        if (SaveAs != null) steps.Add("save_as " + SaveAs + (CloseAfter ? " then close" : ""));
        return steps;
    }
}
