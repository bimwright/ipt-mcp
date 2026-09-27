using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Occurrence pose parser (E3), host-free. Accepted shapes (lengths in mm, angles in degrees):
/// <list type="bullet">
/// <item><c>{ origin_mm: [x,y,z], rotation_deg: [rx,ry,rz] }</c> — R = Rx·Ry·Rz (same convention as place_occurrence)</item>
/// <item><c>{ origin_mm, x_axis: [..], y_axis: [..] }</c> — z = x × y; axes are normalised and must be orthogonal</item>
/// <item><c>{ matrix: [16 numbers] }</c> — row-major 4×4, translation (column 4) in mm</item>
/// </list>
/// The result is an origin (cm) plus three unit axes, which the add-in feeds to
/// <c>Matrix.SetCoordinateSystem</c> — unambiguous regardless of Inventor's matrix storage order.
/// </summary>
public sealed class PoseSpec
{
    public double[] OriginCm { get; private set; } = new double[3];
    public double[] X { get; private set; } = { 1, 0, 0 };
    public double[] Y { get; private set; } = { 0, 1, 0 };
    public double[] Z { get; private set; } = { 0, 0, 1 };

    private const double OrthoTolerance = 1e-6;

    public static bool TryParse(JToken? token, string field, out PoseSpec pose, out string? error)
    {
        pose = new PoseSpec();
        error = null;
        if (token is null || token.Type == JTokenType.Null) return true;
        if (token is not JObject o) { error = field + " must be an object {origin_mm, rotation_deg | x_axis+y_axis} or {matrix}"; return false; }

        foreach (var p in o.Properties())
        {
            if (p.Name is not ("origin_mm" or "rotation_deg" or "x_axis" or "y_axis" or "matrix"))
            { error = $"{field}: unknown key '{p.Name}'"; return false; }
        }

        if (o["matrix"] is { Type: not JTokenType.Null } mt)
        {
            if (!TryNumbers(mt, 16, out var m)) { error = field + ".matrix must be 16 numbers (row-major 4x4, translation in mm)"; return false; }
            if (o["origin_mm"] != null || o["rotation_deg"] != null || o["x_axis"] != null)
            { error = field + ": give either matrix or origin_mm/rotation_deg/x_axis, not both"; return false; }
            var x = new[] { m[0], m[4], m[8] };
            var y = new[] { m[1], m[5], m[9] };
            pose.OriginCm = new[] { m[3] / 10.0, m[7] / 10.0, m[11] / 10.0 };
            return pose.SetAxes(x, y, field, out error);
        }

        if (o["origin_mm"] is { Type: not JTokenType.Null } ot)
        {
            if (!TryNumbers(ot, 3, out var org)) { error = field + ".origin_mm must be [x,y,z]"; return false; }
            pose.OriginCm = new[] { org[0] / 10.0, org[1] / 10.0, org[2] / 10.0 };
        }

        var hasRot = o["rotation_deg"] is { Type: not JTokenType.Null };
        var hasAxes = o["x_axis"] is { Type: not JTokenType.Null } || o["y_axis"] is { Type: not JTokenType.Null };
        if (hasRot && hasAxes) { error = field + ": give rotation_deg or x_axis+y_axis, not both"; return false; }
        if (hasRot)
        {
            if (!TryNumbers(o["rotation_deg"]!, 3, out var r)) { error = field + ".rotation_deg must be [rx,ry,rz]"; return false; }
            var rm = Mul(Mul(Rot(0, r[0]), Rot(1, r[1])), Rot(2, r[2]));
            pose.X = new[] { rm[0, 0], rm[1, 0], rm[2, 0] };
            pose.Y = new[] { rm[0, 1], rm[1, 1], rm[2, 1] };
            pose.Z = new[] { rm[0, 2], rm[1, 2], rm[2, 2] };
            return true;
        }
        if (hasAxes)
        {
            if (!TryNumbers(o["x_axis"], 3, out var x) || !TryNumbers(o["y_axis"], 3, out var y))
            { error = field + ": x_axis and y_axis must both be [x,y,z]"; return false; }
            return pose.SetAxes(x, y, field, out error);
        }
        return true;
    }

    private bool SetAxes(double[] x, double[] y, string field, out string? error)
    {
        error = null;
        var nx = Norm(x);
        var ny = Norm(y);
        if (nx < 1e-12 || ny < 1e-12) { error = field + ": axes must be non-zero"; return false; }
        x = Scale(x, 1 / nx);
        y = Scale(y, 1 / ny);
        if (Math.Abs(Dot(x, y)) > OrthoTolerance) { error = field + ": x_axis and y_axis must be orthogonal"; return false; }
        X = x;
        Y = y;
        Z = Cross(x, y);
        return true;
    }

    private static double[,] Rot(int axis, double deg)
    {
        var a = deg * Math.PI / 180.0;
        double c = Math.Cos(a), s = Math.Sin(a);
        return axis switch
        {
            0 => new[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } },
            1 => new[,] { { c, 0, s }, { 0, 1, 0 }, { -s, 0, c } },
            _ => new[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } },
        };
    }

    private static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                for (var k = 0; k < 3; k++)
                    r[i, j] += a[i, k] * b[k, j];
        return r;
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
    private static double[] Scale(double[] a, double k) => new[] { a[0] * k, a[1] * k, a[2] * k };
    private static double[] Cross(double[] a, double[] b) => new[]
    {
        a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0],
    };

    private static bool TryNumbers(JToken? t, int n, out double[] values)
    {
        values = new double[n];
        if (t is not JArray a || a.Count != n) return false;
        for (var i = 0; i < n; i++)
        {
            if (a[i].Type is not (JTokenType.Integer or JTokenType.Float)) return false;
            values[i] = (double)a[i];
            if (double.IsNaN(values[i]) || double.IsInfinity(values[i])) return false;
        }
        return true;
    }
}
