using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// API-agnostic parsers for <c>{x,y,z}</c>/<c>[x,y,z]</c> vector params (fixed work planes,
/// camera, probe points — spec F4). No Inventor types so the test suite runs them without
/// Inventor; callers convert mm→cm where the API needs it.
/// </summary>
public static class Vec3Params
{
    /// <summary>Parse a <c>{x,y,z}</c> object or <c>[x,y,z]</c> array into an xyz triple.</summary>
    public static bool TryParse(JToken? token, string fieldName, out double x, out double y, out double z, out string error)
    {
        x = y = z = 0;
        error = "";

        // Reject non-finite components here (net48-safe IsNaN/IsInfinity — double.IsFinite
        // does not exist on .NET Framework) so NaN/Infinity never reach the Inventor API.
        double? Read(JToken? t)
        {
            if (t is null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return null;
            var v = t.Value<double>();
            return double.IsNaN(v) || double.IsInfinity(v) ? null : v;
        }

        if (token is JObject obj)
        {
            var vx = Read(obj["x"]); var vy = Read(obj["y"]); var vz = Read(obj["z"]);
            if (vx is null || vy is null || vz is null)
            {
                error = $"{fieldName} must be {{x,y,z}} with numeric components";
                return false;
            }
            x = vx.Value; y = vy.Value; z = vz.Value;
            return true;
        }
        if (token is JArray arr && arr.Count == 3)
        {
            var vx = Read(arr[0]); var vy = Read(arr[1]); var vz = Read(arr[2]);
            if (vx is null || vy is null || vz is null)
            {
                error = $"{fieldName} must be [x,y,z] with numeric components";
                return false;
            }
            x = vx.Value; y = vy.Value; z = vz.Value;
            return true;
        }

        error = $"{fieldName} must be {{x,y,z}} or [x,y,z]";
        return false;
    }

    /// <summary>
    /// True when the two axes cannot span a plane: either is (near) zero-length or they are
    /// (near) parallel — sin(angle) = |x×y| / (|x||y|) below 1e-6. Inventor's AddFixed would
    /// fail on these anyway; rejecting here gives a better error.
    /// </summary>
    public static bool AxesDegenerate(double[] xAxis, double[] yAxis)
    {
        double cx = xAxis[1] * yAxis[2] - xAxis[2] * yAxis[1];
        double cy = xAxis[2] * yAxis[0] - xAxis[0] * yAxis[2];
        double cz = xAxis[0] * yAxis[1] - xAxis[1] * yAxis[0];
        var cross = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        var lx = Math.Sqrt(xAxis[0] * xAxis[0] + xAxis[1] * xAxis[1] + xAxis[2] * xAxis[2]);
        var ly = Math.Sqrt(yAxis[0] * yAxis[0] + yAxis[1] * yAxis[1] + yAxis[2] * yAxis[2]);
        if (lx < 1e-12 || ly < 1e-12) return true;
        return cross / (lx * ly) < 1e-6;
    }
}
