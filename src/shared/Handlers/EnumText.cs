using System;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// API-agnostic enum-name cleanup for query DTOs: Inventor enum members stringify as
/// <c>kExtrudeFeatureObject</c> / <c>kUpToDateHealth</c>; the MCP surface reports the friendlier
/// core (<c>ExtrudeFeature</c>, <c>UpToDate</c>) by stripping the leading <c>k</c> and a trailing
/// suffix. Pure string work so the test suite exercises it without Inventor.
/// </summary>
public static class EnumText
{
    /// <summary>Strip the leading <c>k</c> and <paramref name="suffix"/> from an enum member name.</summary>
    public static string Friendly(string enumName, string suffix = "Object")
    {
        var s = enumName ?? "";
        if (s.StartsWith("k", StringComparison.Ordinal) && s.Length > 1) s = s.Substring(1);
        if (s.EndsWith(suffix, StringComparison.Ordinal) && s.Length > suffix.Length)
            s = s.Substring(0, s.Length - suffix.Length);
        return s;
    }
}
