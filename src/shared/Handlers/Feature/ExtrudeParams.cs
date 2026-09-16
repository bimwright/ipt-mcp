using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// API-agnostic parameter checks for the <c>extrude</c> wire command (spec F4-P0). Kept free of
/// Inventor types so the test suite exercises them without Inventor; the handler calls these and
/// only then touches the API.
/// </summary>
public static class ExtrudeParams
{
    /// <summary>
    /// <c>distance_mm</c> accepts a number (mm) or a string: a bare numeric string is treated as
    /// mm, anything else is passed to Inventor verbatim as a parameter expression
    /// (<c>"40 mm"</c>, <c>"plate_thk"</c>). True when a usable distance was parsed —
    /// <paramref name="distanceMm"/> set for the numeric path, <paramref name="expression"/> for
    /// the expression path (exactly one is non-null).
    /// </summary>
    public static bool TryParseDistance(JToken? token, out double? distanceMm, out string? expression, out string error)
    {
        distanceMm = null;
        expression = null;
        error = "";

        if (token is null || token.Type == JTokenType.Null)
        {
            error = "sketch_name and distance_mm are required";
            return false;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            var d = token.Value<double>();
            if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
            {
                error = "distance_mm must be a finite number greater than 0";
                return false;
            }
            distanceMm = d;
            return true;
        }

        if (token.Type == JTokenType.String)
        {
            var s = ((string)token)!.Trim();
            if (s.Length == 0)
            {
                error = "distance_mm must not be empty";
                return false;
            }
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
                {
                    error = "distance_mm must be a finite number greater than 0";
                    return false;
                }
                distanceMm = d;
                return true;
            }
            expression = s;
            return true;
        }

        error = "distance_mm must be a number (mm) or an expression string";
        return false;
    }

    /// <summary>
    /// <c>affected_bodies</c> only has meaning for join/cut/intersect — combining it with
    /// <c>new_body</c> is a caller bug, rejected before touching the API.
    /// </summary>
    public static bool TryRejectIncompatible(string? operation, JArray? affectedBodies, out string error)
    {
        error = "";
        var isNewBody = string.Equals(operation?.Trim(), "new_body", StringComparison.OrdinalIgnoreCase)
            || string.Equals(operation?.Trim(), "newbody", StringComparison.OrdinalIgnoreCase);
        if (isNewBody && affectedBodies is not null && affectedBodies.Count > 0)
        {
            error = "affected_bodies has no meaning with operation=new_body; drop it or use join|cut|intersect";
            return true;
        }
        return false;
    }
}
