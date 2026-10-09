using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// API-agnostic parameter checks for the <c>add_sketch_dimension</c> wire command, kept free of
/// Inventor types so the test suite exercises them without Inventor.
/// </summary>
public static class SketchDimensionParams
{
    /// <summary>
    /// <c>value_mm</c> is a number (mm) or an Inventor expression (<c>"Shaft_D / 2"</c>). Exactly one
    /// of <paramref name="valueMm"/> / <paramref name="expression"/> is set on success. A numeric
    /// value must be greater than 0; expressions are validated by Inventor in the handler.
    /// </summary>
    public static bool TryParseValue(JToken? token, out double? valueMm, out string? expression, out string error)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            valueMm = null;
            expression = null;
            error = "entity_id and value_mm are required";
            return false;
        }

        if (!NumberOrExpression.TryParse(token, "value_mm", "mm", out valueMm, out expression, out error))
            return false;
        if (valueMm is <= 0)
        {
            valueMm = null;
            error = "value_mm must be greater than 0";
            return false;
        }
        return true;
    }
}
