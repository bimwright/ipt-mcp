#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

internal static class ParameterValueDto
{
    public static JObject From(Parameter prm, bool includeKind = true)
    {
        return FromCore(
            prm.Name,
            prm.Expression,
            prm.get_Units(),
            includeKind ? prm.ParameterType.ToString() : null,
            () => prm.Value);
    }

    public static JObject From(UserParameter prm, bool includeKind = true)
    {
        return FromCore(
            prm.Name,
            prm.Expression,
            prm.get_Units(),
            includeKind ? prm.ParameterType.ToString() : null,
            () => prm.Value);
    }

    private static JObject FromCore(string name, string expression, string unit, string? kind, Func<object?> value)
    {
        var o = new JObject
        {
            ["name"] = name,
            ["expression"] = expression,
            ["unit"] = unit,
        };
        if (!string.IsNullOrEmpty(kind))
            o["kind"] = kind;

        object? raw = null;
        try { raw = value(); } catch { }
        if (raw is not double d)
        {
            o["evaluated"] = JValue.CreateNull();
            return o;
        }

        AddConvertedValue(o, d, unit);
        return o;
    }

    private static void AddConvertedValue(JObject o, double internalValue, string? unit)
    {
        var u = (unit ?? "").Trim().ToLowerInvariant();
        if (IsAngle(u))
        {
            o["value_deg"] = UnitConvert.RadToDeg(internalValue);
            return;
        }

        if (IsVolume(u))
        {
            o["value_mm3"] = UnitConvert.Cm3ToMm3(internalValue);
            return;
        }

        if (IsArea(u))
        {
            o["value_mm2"] = UnitConvert.Cm2ToMm2(internalValue);
            return;
        }

        if (IsLength(u))
        {
            o["value_mm"] = UnitConvert.CmToMm(internalValue);
            return;
        }

        o["value_unitless"] = internalValue;
    }

    private static bool IsAngle(string u) => u.Contains("deg") || u.Contains("rad");

    private static bool IsArea(string u) =>
        u.Contains("^2") || u.Contains("**2") || u.Contains("area");

    private static bool IsVolume(string u) =>
        u.Contains("^3") || u.Contains("**3") || u.Contains("volume");

    private static bool IsLength(string u)
    {
        if (string.IsNullOrWhiteSpace(u) || u == "ul") return false;
        return u.Contains("mm") || u.Contains("cm") || u.Contains("meter") || u == "m" ||
               u.Contains("in") || u.Contains("ft") || u.Contains("mil");
    }
}
#endif
