using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Tools;

/// <summary>Free-form JSON tool arguments (selectors, poses, recipes) → Newtonsoft tokens for the wire.</summary>
internal static class JsonArg
{
    public static JToken? From(JsonElement? e)
        => e is { } v && v.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ? JToken.Parse(v.GetRawText()) : null;

    public const string SelectorDoc =
        "Occurrence selector: {names?:[glob], regex?, file?:glob, path_contains?, leaf?:false, max_depth?, include_suppressed?:false, limit?} " +
        "or a name / array of names. Globs use * and ?, case-insensitive; a name glob with '/' matches the occurrence path (SUB:1/PART:2), " +
        "file matches the referenced file name. leaf=true keeps only part occurrences at any depth. Zero matches or more than limit is an error " +
        "(with the closest names) — never a silent cut.";

    public const string DocumentDoc =
        "document: full path or name of an assembly already open/loaded in Inventor (never opened automatically); omit for the active document.";
}
