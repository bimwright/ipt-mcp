using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// API-agnostic parsing/validation for the <c>set_camera</c> wire command (spec F4-P0-4).
/// <c>eye</c>/<c>target</c>/<c>up</c> reuse <see cref="Vec3Params"/> ({x,y,z} or [x,y,z], mm for
/// positions); <c>extents_mm</c> is [width,height] or {width,height}; <c>perspective</c>/<c>fit</c>
/// are plain bools. At least one parameter is required. Degenerate views are rejected here when
/// the request carries enough data to detect them: eye==target, zero-length up, up parallel to the
/// eye→target direction. No Inventor types — the test suite runs this without Inventor.
/// </summary>
public static class CameraParams
{
    public sealed class Spec
    {
        public double[]? Eye;        // mm
        public double[]? Target;     // mm
        public double[]? Up;         // direction vector (unitless)
        public bool? Perspective;
        public double[]? ExtentsMm;  // [width, height] mm
        public bool Fit;

        public bool HasAny =>
            Eye is not null || Target is not null || Up is not null ||
            Perspective.HasValue || ExtentsMm is not null || Fit;
    }

    public static bool TryParse(JObject p, out Spec spec, out string error)
    {
        spec = new Spec();
        error = "";

        if (!Vec(p, "eye", out var eye, out error)) return false;
        if (!Vec(p, "target", out var target, out error)) return false;
        if (!Vec(p, "up", out var up, out error)) return false;
        spec.Eye = eye; spec.Target = target; spec.Up = up;

        if (!Bool(p, "perspective", out var perspective, out error)) return false;
        spec.Perspective = perspective;
        if (!Bool(p, "fit", out var fit, out error)) return false;
        spec.Fit = fit ?? false;

        if (!Extents(p, out var extents, out error)) return false;
        spec.ExtentsMm = extents;

        if (!spec.HasAny)
        {
            error = "at least one of eye, target, up, perspective, extents_mm, fit is required";
            return false;
        }
        if (eye is not null && target is not null && Same(eye, target))
        {
            error = "eye and target must differ (zero-length view direction)";
            return false;
        }
        if (up is not null)
        {
            var len2 = up[0] * up[0] + up[1] * up[1] + up[2] * up[2];
            if (len2 < 1e-24)
            {
                error = "up must be a non-zero direction vector";
                return false;
            }
            if (eye is not null && target is not null)
            {
                var viewDir = new[] { target[0] - eye[0], target[1] - eye[1], target[2] - eye[2] };
                if (Vec3Params.AxesDegenerate(up, viewDir))
                {
                    error = "up must not be parallel to the eye→target view direction";
                    return false;
                }
            }
        }
        return true;
    }

    private static bool Vec(JObject p, string name, out double[]? v, out string error)
    {
        v = null;
        var t = p[name];
        if (t is null || t.Type is JTokenType.Null or JTokenType.Undefined) { error = ""; return true; }
        if (!Vec3Params.TryParse(t, name, out var x, out var y, out var z, out error)) return false;
        v = new[] { x, y, z };
        return true;
    }

    private static bool Bool(JObject p, string name, out bool? v, out string error)
    {
        v = null;
        var t = p[name];
        if (t is null || t.Type is JTokenType.Null or JTokenType.Undefined) { error = ""; return true; }
        if (t.Type != JTokenType.Boolean)
        {
            error = $"{name} must be a boolean";
            return false;
        }
        v = t.Value<bool>();
        error = "";
        return true;
    }

    private static bool Extents(JObject p, out double[]? v, out string error)
    {
        v = null;
        error = "";
        var t = p["extents_mm"];
        if (t is null || t.Type is JTokenType.Null or JTokenType.Undefined) return true;

        double? w = null, h = null;
        if (t is JArray a && a.Count == 2)
        {
            w = Num(a[0]); h = Num(a[1]);
        }
        else if (t is JObject o)
        {
            w = Num(o["width"]); h = Num(o["height"]);
        }
        if (w is null || h is null || w <= 0 || h <= 0)
        {
            error = "extents_mm must be [width,height] or {width,height} with positive numeric components";
            return false;
        }
        v = new[] { w.Value, h.Value };
        return true;
    }

    private static double? Num(JToken? t)
    {
        if (t is null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return null;
        var d = t.Value<double>();
        return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
    }

    private static bool Same(double[] a, double[] b)
        => Math.Abs(a[0] - b[0]) < 1e-9 && Math.Abs(a[1] - b[1]) < 1e-9 && Math.Abs(a[2] - b[2]) < 1e-9;
}
