#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Scripting;

/// <summary>
/// Helpers for <c>send_code</c> scripts (<c>script_helpers_v1</c>). The namespace is imported into every script, so a
/// script calls <c>Ipt.Doc("A.iam")</c>, <c>Ipt.Occs(asm, new { regex = "^VENT_" })</c>, <c>Ipt.BboxMm(o)</c>. Lengths in
/// and out are millimetres, angles degrees. Lookups return real Inventor objects, so a script can find and then edit
/// in one call; numbers meant for output are cleaned to 1e-6 mm. A failed lookup throws <see cref="IptException"/>
/// naming what exists instead. Reuses the typed tools' logic (occurrence selector, named references, face selector).
/// </summary>
public static partial class Ipt
{
    /// <summary>Inventor.Application for the running script. Set by the add-in around each run.</summary>
    public static Application App
    {
        get => _app ?? throw new IptException("Ipt helpers are only available while a send_code script runs.");
        internal set => _app = value;
    }

    [ThreadStatic] private static Application? _app;

    internal static void Detach() => _app = null;

    /// <summary>Length tolerance for <see cref="Eq"/>, <see cref="Same"/> and the cleaned outputs (mm).</summary>
    public const double TolMm = 1e-6;

    /// <summary>Angle tolerance for <see cref="Parallel"/> (degrees).</summary>
    public const double TolDeg = 1e-6;

    private const int OutputDecimals = 6;
    private const int MaxCandidates = 20;

    // ---- units -------------------------------------------------------------------------------------------------

    /// <summary>Inventor internal centimetres → millimetres.</summary>
    public static double Mm(double cm) => cm * 10.0;

    /// <summary>Millimetres → Inventor internal centimetres.</summary>
    public static double Cm(double mm) => mm / 10.0;

    public static double Deg(double rad) => rad * 180.0 / Math.PI;

    public static double Rad(double deg) => deg * Math.PI / 180.0;

    // ---- numbers and tolerance ---------------------------------------------------------------------------------

    /// <summary>Rounds to <paramref name="decimals"/> places and turns -0 and values below 1e-9 into 0.</summary>
    public static double Clean(double value, int decimals = OutputDecimals)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value;
        var r = Math.Round(value, Math.Max(0, Math.Min(15, decimals)));
        return Math.Abs(r) < 1e-9 ? 0d : r;
    }

    public static bool Eq(double a, double b, double tol = TolMm) => Math.Abs(a - b) <= tol;

    public static bool IsZero(double value, double tol = TolMm) => Math.Abs(value) <= tol;

    /// <summary>Snaps to the nearest multiple of <paramref name="step"/> (for example 0.5 mm).</summary>
    public static double Snap(double value, double step)
    {
        if (step <= 0) throw new IptException("Snap step must be greater than 0.");
        return Clean(Math.Round(value / step) * step);
    }

    /// <summary>True when two points (Inventor Point, or [x,y,z] in mm) are within <paramref name="tolMm"/>.</summary>
    public static bool Same(object a, object b, double tolMm = TolMm)
    {
        var p = RawMm(a);
        var q = RawMm(b);
        var dx = p[0] - q[0]; var dy = p[1] - q[1]; var dz = p[2] - q[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= tolMm;
    }

    /// <summary>True when two directions (Vector, UnitVector or [x,y,z]) are parallel or anti-parallel within <paramref name="tolDeg"/>.</summary>
    public static bool Parallel(object a, object b, double tolDeg = TolDeg)
    {
        var u = Direction(a);
        var v = Direction(b);
        var lu = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        var lv = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (lu == 0 || lv == 0) throw new IptException("Parallel needs two non-zero directions.");
        var cos = Math.Abs((u[0] * v[0] + u[1] * v[1] + u[2] * v[2]) / (lu * lv));
        return Deg(Math.Acos(Math.Min(1.0, cos))) <= tolDeg;
    }

    // ---- loops -------------------------------------------------------------------------------------------------

    /// <summary>Stops the script when its time budget is spent. The add-in already calls this at the top of every loop.</summary>
    public static void Check() => IptGuard.Check("Ipt.Check()", 0);

    // ---- discovery ---------------------------------------------------------------------------------------------

    /// <summary>Every Ipt helper signature, for discovery: <c>return Ipt.Help();</c></summary>
    public static string[] Help() => typeof(Ipt).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName)
        .Select(Signature)
        .Distinct()
        .OrderBy(s => s, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Real members of an Inventor API type (or an Ipt type) with signatures, read from the interop this add-in loaded;
    /// an enum lists its values. <paramref name="filter"/> keeps the lines containing it (case-insensitive). Look a member
    /// up here instead of guessing it: <c>return Ipt.Api("DrawingCurve", "color");</c>
    /// </summary>
    public static string[] Api(string type, string? filter = null) =>
        global::Bimwright.Ipt.Shared.Contracts.ApiMemberHints.Describe(type, filter,
            global::Bimwright.Ipt.Shared.Handlers.Code.SendCodeHandler.ApiTypes.Value);

    private static string Signature(MethodInfo m) =>
        $"{TypeName(m.ReturnType)} Ipt.{m.Name}{(m.IsGenericMethod ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "")}(" +
        string.Join(", ", m.GetParameters().Select(p =>
            (p.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : "") + TypeName(p.ParameterType) + " " + p.Name +
            (p.IsOptional ? " = " + (p.DefaultValue is string s ? "\"" + s + "\"" : p.DefaultValue?.ToString() ?? "null") : ""))) + ")";

    private static string TypeName(Type t)
    {
        if (t == typeof(void)) return "void";
        if (t == typeof(object)) return "object";
        if (t == typeof(string)) return "string";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(int)) return "int";
        if (t.IsArray) return TypeName(t.GetElementType()!) + "[]";
        if (Nullable.GetUnderlyingType(t) is { } n) return TypeName(n) + "?";
        if (t.IsGenericType)
        {
            var name = t.Name.Substring(0, t.Name.IndexOf('`'));
            if (name == "ValueTuple") return "(" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ")";
            return name + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
        }
        return t.DeclaringType == typeof(Ipt) ? "Ipt." + t.Name : t.Name;
    }

    // ---- shared internals --------------------------------------------------------------------------------------

    internal static TransientGeometry Tg => App.TransientGeometry;

    internal static string List(IEnumerable<string> names)
    {
        var all = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (all.Count == 0) return "(none)";
        var shown = string.Join(", ", all.Take(MaxCandidates));
        return all.Count > MaxCandidates ? $"{shown}, … ({all.Count} in total)" : shown;
    }

    internal static double[] CleanMm(double xCm, double yCm, double zCm) =>
        new[] { Clean(Mm(xCm)), Clean(Mm(yCm)), Clean(Mm(zCm)) };

    /// <summary>[x,y,z] in mm, not rounded: from a Point, Vertex, WorkPoint, or a numeric array already in mm.</summary>
    internal static double[] RawMm(object point)
    {
        switch (point)
        {
            case Point p: return new[] { Mm(p.X), Mm(p.Y), Mm(p.Z) };
            case Vertex v: return RawMm(v.Point);
            case WorkPoint w: return RawMm(w.Point);
            case double[] { Length: 3 } d: return d;
            case IEnumerable<double> e when e.Count() == 3: return e.ToArray();
            default: throw new IptException($"expected a Point, Vertex, WorkPoint or [x,y,z] in mm, got {Describe(point)}.");
        }
    }

    /// <summary>
    /// A spec/selector as this assembly's JToken. Inventor loads its own Newtonsoft.Json, so a JObject made in the script
    /// (or <c>data["…"]</c>) can be a different type from ours: those go through their JSON text. Anything else (an
    /// anonymous object, a dictionary) is serialized. No public helper takes or returns a Newtonsoft type for this reason.
    /// </summary>
    internal static JToken? Json(object? value)
    {
        switch (value)
        {
            case null: return null;
            case JToken t: return t;
        }
        var type = value.GetType();
        if (type.FullName?.StartsWith("Newtonsoft.Json.Linq.", StringComparison.Ordinal) != true) return JToken.FromObject(value);
        if (type.Name == "JValue") return new JValue(type.GetProperty("Value")?.GetValue(value));
        return JToken.Parse(value.ToString()!);
    }

    private static double[] Direction(object direction) => direction switch
    {
        UnitVector u => new[] { u.X, u.Y, u.Z },
        Vector v => new[] { v.X, v.Y, v.Z },
        double[] { Length: 3 } d => d,
        _ => throw new IptException($"expected a Vector, UnitVector or [x,y,z], got {Describe(direction)}."),
    };

    internal static string Describe(object? value)
    {
        if (value is null) return "null";
        try
        {
            switch (value)
            {
                case Document d: return $"document '{d.DisplayName}'";
                case ComponentOccurrence o: return $"occurrence '{o.Name}'";
                case string s: return $"\"{s}\"";
            }
        }
        catch { }
        var t = value.GetType();
        return t.IsCOMObject ? "an Inventor object" : t.Name;
    }
}
#endif
