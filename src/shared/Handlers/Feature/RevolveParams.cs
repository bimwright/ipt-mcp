using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Feature;

/// <summary>
/// API-agnostic parameter checks for the <c>revolve</c> wire command, kept free of Inventor types
/// so the test suite exercises them without Inventor.
/// </summary>
public static class RevolveParams
{
    /// <summary>
    /// <c>angle_deg</c> is a number (degrees) or an Inventor expression (<c>"sweep_angle"</c>).
    /// Numbers keep their existing meaning — 0 or ±360 is a full revolve, a negative angle runs in
    /// the negative direction — so no sign check here.
    /// </summary>
    public static bool TryParseAngle(JToken? token, out double? angleDeg, out string? expression, out string error)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            angleDeg = null;
            expression = null;
            error = "sketch_name, axis_id and angle_deg are required";
            return false;
        }
        return NumberOrExpression.TryParse(token, "angle_deg", "degrees", out angleDeg, out expression, out error);
    }
}
