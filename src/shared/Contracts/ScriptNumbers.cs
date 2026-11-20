using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Removes binary floating-point noise from numbers a <c>send_code</c> script returns (<c>numbers: "clean_v1"</c>):
/// 12 significant digits (2388.400000000001 → 2388.4) and |x| below 1e-9 → 0 (1.2e-16 → 0). At that size the value
/// carries no meaning in any unit scripts return (mm, cm, cm², radians). Only the reported numbers change; the model
/// is never rounded. Callers that need the raw doubles pass <c>raw_numbers: true</c>. Host-free.
/// </summary>
public static class ScriptNumbers
{
    public const string Mode = "clean_v1";
    public const int SignificantDigits = 12;
    public const double ZeroBelow = 1e-9;

    public static double Clean(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value;
        if (Math.Abs(value) < ZeroBelow) return 0d;
        return double.Parse(value.ToString("G" + SignificantDigits, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Cleans every double/float value in the token in place and returns it. Integers, decimals and strings are left alone.</summary>
    public static JToken? Clean(JToken? token)
    {
        switch (token)
        {
            case null:
                return null;
            case JValue v when v.Type == JTokenType.Float && v.Value is double d:
                v.Value = Clean(d);
                return v;
            case JValue v when v.Type == JTokenType.Float && v.Value is float f:
                // Via the float's own shortest text: (double)0.1f is 0.100000001490116, not 0.1.
                v.Value = Clean(double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
                return v;
            case JContainer c:
                foreach (var child in c.Children()) Clean(child);
                return c;
            default:
                return token;
        }
    }
}
