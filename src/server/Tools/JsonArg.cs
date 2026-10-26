using System.Linq;
using System.Text.Json;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>Free-form JSON tool arguments (selectors, poses, recipes) → Newtonsoft tokens for the wire.</summary>
internal static class JsonArg
{
    public static JToken? From(JsonElement? e)
        => e is { } v && v.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ? WireJson.Load(v.GetRawText()) : null;

    /// <summary>
    /// send_code <c>data</c> read the way the add-in prelude reads <c>data_json</c>: strings stay strings, integers
    /// stay exact, other numbers must be finite doubles, one JSON value, no duplicate keys. Its compact text is what
    /// travels as <c>data_json</c> and what <c>data_hash</c> covers, so equal hashes mean the script sees equal data.
    /// </summary>
    internal static bool TryData(string? json, out JToken? data, out string? error)
    {
        data = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var reader = WireJson.Reader(json);
            var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read())
            {
                error = "data must be a single JSON value";
                return false;
            }
            var bad = ((token as JContainer)?.DescendantsAndSelf() ?? new[] { token }).OfType<JValue>()
                .FirstOrDefault(v => v.Value is double d && (double.IsInfinity(d) || double.IsNaN(d)));
            if (bad != null)
            {
                error = $"data number at '{bad.Path}' is not a finite double; send it as a string";
                return false;
            }
            data = token.Type == JTokenType.Null ? null : token;
            return true;
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            error = "data is not valid JSON: " + ex.Message;
            return false;
        }
    }

    internal static bool TryData(JsonElement? e, out JToken? data, out string? error)
    {
        if (e is not { } v || v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            data = null;
            error = null;
            return true;
        }
        return TryData(v.GetRawText(), out data, out error);
    }

    public const string SelectorDoc =
        "Occurrence selector: {names?:[glob], regex?, file?:glob, path_contains?, leaf?:false, max_depth?, include_suppressed?:false, limit?} " +
        "or a name / array of names. Globs use * and ?, case-insensitive; a name glob with '/' matches the occurrence path (SUB:1/PART:2), " +
        "file matches the referenced file name. leaf=true keeps only part occurrences at any depth. Zero matches or more than limit is an error " +
        "(with the closest names) — never a silent cut.";

    public const string DocumentDoc =
        "document: full path or name of an assembly already open/loaded in Inventor (never opened automatically); omit for the active document.";
}
