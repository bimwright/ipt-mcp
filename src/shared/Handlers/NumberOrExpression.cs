using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// API-agnostic parse for wire values that take either a number or an Inventor parameter
/// expression (sketch dimension value, revolve angle). A JSON number or a
/// numeric string is the number path; any other non-blank string is passed to Inventor verbatim
/// (trimmed) as an expression such as <c>"plate_w / 2"</c> or <c>"40 mm"</c>. Sign and range
/// rules differ per command, so callers apply them to the number path.
/// </summary>
public static class NumberOrExpression
{
    public static bool TryParse(JToken? token, string field, string unit,
        out double? number, out string? expression, out string error)
    {
        number = null;
        expression = null;
        error = "";

        if (token is null || token.Type == JTokenType.Null)
        {
            error = $"{field} is required";
            return false;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            return TryFinite(token.Value<double>(), field, out number, out error);

        if (token.Type == JTokenType.String)
        {
            var s = ((string)token)!.Trim();
            if (s.Length == 0)
            {
                error = $"{field} must not be empty";
                return false;
            }
            // Numeric strings stay on the number path so "40" keeps meaning the tool's unit (mm/deg)
            // rather than Inventor's document default unit.
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return TryFinite(d, field, out number, out error);
            // .NET Framework rejects overflow in TryParse, whereas newer runtimes return Infinity.
            // Neither result should turn an overflowing numeric literal into an Inventor expression.
            if (Regex.IsMatch(s, @"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant))
            {
                error = $"{field} must be a finite number";
                return false;
            }
            expression = s;
            return true;
        }

        error = $"{field} must be a number ({unit}) or an expression string";
        return false;
    }

    private static bool TryFinite(double d, string field, out double? number, out string error)
    {
        number = null;
        error = "";
        // "1e400" parses to Infinity and NaN slips past any d<=0 check, so test finiteness explicitly.
        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            error = $"{field} must be a finite number";
            return false;
        }
        number = d;
        return true;
    }
}
