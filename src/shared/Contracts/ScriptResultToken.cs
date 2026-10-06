using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Converts a <c>send_code</c> script's return value (Roslyn <c>ScriptState.ReturnValue</c>)
/// into a JSON-safe token for <c>data.result</c> (spec F2-a). API-agnostic so the server/test
/// assemblies compile it; <c>SendCodeHandler</c> is the only caller.
/// </summary>
internal static class ScriptResultToken
{
    internal const string ApiObjectError =
        "return a DTO (anonymous object / primitives / arrays), not an Inventor API object";

    /// <summary>
    /// Returns the token to store under <c>data.result</c>, or null when <paramref name="value"/>
    /// is null. On failure <paramref name="error"/> carries the agent-facing message and the
    /// handler should mark <c>data.ok = false</c>.
    /// </summary>
    internal static JToken? ToResultToken(object? value, out string? error)
    {
        error = null;
        switch (value)
        {
            case null:
                return null;
            case JToken token:
                return token;
        }

        if (IsApiObject(value))
        {
            error = ApiObjectError;
            return null;
        }

        try
        {
            return JToken.FromObject(value);
        }
        catch (Exception ex)
        {
            error = $"return value is not JSON-serializable ({ex.GetType().Name}); {ApiObjectError}";
            return null;
        }
    }

    private static bool IsApiObject(object value)
    {
        var t = value.GetType();
        // Type.IsCOMObject covers RCWs on every plugin TFM (Marshal.IsComObject is obsolete on
        // net8+); the namespace check also catches non-RCW Inventor interop wrapper types.
        return t.IsCOMObject ||
               (t.Namespace?.StartsWith("Inventor", StringComparison.Ordinal) ?? false);
    }
}
