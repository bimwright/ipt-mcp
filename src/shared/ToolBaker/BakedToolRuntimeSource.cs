using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.ToolBaker;

/// <summary>
/// Serializes the runtime descriptor (preset / macro / script) persisted as a baked tool's <c>SourceCode</c>.
/// API-agnostic; ported from nwd-mcp.
/// </summary>
public static class BakedToolRuntimeSource
{
    public static string BuildPreset(string handlerTool, JObject? fixedArgs)
    {
        return new JObject
        {
            ["kind"] = "preset",
            ["handler_tool"] = handlerTool,
            ["fixed_args"] = fixedArgs ?? new JObject()
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>A send_code script baked as a tool: its runtime params arrive as the script's <c>data</c>.</summary>
    public static string BuildScript(string code, string[] modules, string key)
    {
        return new JObject
        {
            ["kind"] = "script",
            ["code"] = code,
            ["modules"] = new JArray(modules ?? new string[0]),
            ["key"] = key
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    public static bool TryReadScript(string? sourceCode, out string code, out string[] modules, out string key)
    {
        code = "";
        modules = new string[0];
        key = "";
        try
        {
            if (string.IsNullOrWhiteSpace(sourceCode) || JToken.Parse(sourceCode!) is not JObject o || (string?)o["kind"] != "script")
                return false;
            code = (string?)o["code"] ?? "";
            modules = (o["modules"] as JArray)?.Values<string>().Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!).ToArray() ?? new string[0];
            key = (string?)o["key"] ?? "";
            return code.Length > 0;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }
    }

    public static string BuildMacro(string[] sequence)
    {
        return new JObject
        {
            ["kind"] = "macro",
            ["sequence"] = new JArray(sequence ?? new string[0])
        }.ToString(Newtonsoft.Json.Formatting.None);
    }
}
