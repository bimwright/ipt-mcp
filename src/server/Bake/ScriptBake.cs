using System;
using System.Linq;
using Bimwright.Ipt.Shared.Security;
using Bimwright.Ipt.Shared.ToolBaker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Bake;

/// <summary>
/// Script bake: a send_code script that takes its inputs as <c>data</c> is already a parameterised
/// tool (code = template, data = params), so it can be baked without rewriting it. Each such call
/// records hashes only — the script key (code + module names) and the data hash, never the code.
/// With adaptive bake on, once one script has succeeded with <see cref="MinDistinctData"/> different
/// data inside <see cref="Window"/>, a suggestion opens and send_code returns a <c>bake_hint</c>
/// to accept it; accepting requires the same code again (hash-checked) and only then stores it.
/// When an accepted script tool already exists for the code, the hint points at that tool instead.
/// Bake bookkeeping never fails or alters the send_code result itself.
/// </summary>
internal static class ScriptBake
{
    internal const string Source = "script";
    internal const string UsageTool = "send_code";
    internal const int MinDistinctData = 3;
    internal static readonly TimeSpan Window = TimeSpan.FromDays(10);

    internal static string Key(string code, string[]? modules)
        => "script:" + BakeRedactor.HashBody(code + "\n//modules:" + string.Join(",", modules ?? Array.Empty<string>()));

    /// <summary>Records one send_code call that carried data; returns a bake_hint for its response, or null.</summary>
    internal static JObject? Observe(InventorMcpConfig config, string code, string[]? modules, JToken data, bool ok, long durationMs)
    {
        if (!config.EnableToolBaker) return null;
        try
        {
            var key = Key(code, modules);
            BakePaths.EnsureDir(config);
            using var db = new BakeDb(BakePaths.Db(config));
            db.Migrate();

            if (FindTool(db, key) is { } tool)
                return new JObject
                {
                    ["tool"] = "inventor_run_baked_tool",
                    ["args_sketch"] = new JObject { ["name"] = tool.Name, ["paramsJson"] = "<the data of this call, as JSON>" },
                    ["reason"] = $"This script is the baked tool '{tool.Name}'; call it with only the data instead of resending the code.",
                };
            if (!config.EnableAdaptiveBake) return null;

            db.InsertUsageEvent(new UsageEvent
            {
                Tool = UsageTool,
                ParamsHash = key,
                DataHash = BakeRedactor.HashBody(data.ToString(Formatting.None)),
                Success = ok,
                DurationMs = durationMs,
            });
            if (!ok) return null;
            var (runs, distinct) = db.CountSuccessfulRuns(UsageTool, key, DateTimeOffset.UtcNow - Window);
            if (distinct < MinDistinctData) return null;

            var suggestion = db.ListSuggestions().FirstOrDefault(s => s.ClusterKey == key);
            if (suggestion == null)
            {
                var now = DateTimeOffset.UtcNow.ToString("o");
                suggestion = new BakeSuggestionRecord
                {
                    Id = "sug_" + Guid.NewGuid().ToString("N"),
                    ClusterKey = key,
                    Source = Source,
                    Title = $"Bake a send_code script that ran with {distinct} different data inputs",
                    Description = "send_code script (code + modules) repeatedly run with different data; baked, it is called with only the data.",
                    State = BakeSuggestionStates.Open,
                    Score = distinct,
                    CreatedAt = now,
                    UpdatedAt = now,
                    PayloadJson = new JObject
                    {
                        ["script_key"] = key,
                        ["modules"] = new JArray(modules ?? Array.Empty<string>()),
                        ["code_length"] = code.Length,
                        ["data_type"] = JsonType(data),
                        ["output_choices"] = new JArray("mcp_only"),
                    }.ToString(Formatting.None),
                };
                db.UpsertSuggestion(suggestion);
            }
            if (suggestion.State != BakeSuggestionStates.Open) return null;
            return new JObject
            {
                ["tool"] = "inventor_accept_bake_suggestion",
                ["args_sketch"] = new JObject
                {
                    ["suggestionId"] = suggestion.Id,
                    ["desiredName"] = "<snake_case tool name>",
                    ["code"] = "<this exact script>",
                    ["modules"] = new JArray(modules ?? Array.Empty<string>()),
                },
                ["reason"] = $"This script succeeded {runs} times with {distinct} different data inputs. Accepting bakes it into a tool called with only the data (inventor_run_baked_tool); the code must match this script and is stored only on accept.",
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static BakedToolRecord? FindTool(BakeDb db, string key)
        => db.ReadRegistryRecords().FirstOrDefault(r => r.Source == Source
            && BakedToolRuntimeSource.TryReadScript(r.SourceCode, out _, out _, out var k) && k == key);

    /// <summary>JSON Schema type name of a data sample (the baked tool's params schema).</summary>
    internal static string JsonType(JToken? data) => data?.Type switch
    {
        JTokenType.Object => "object",
        JTokenType.Array => "array",
        JTokenType.Integer or JTokenType.Float => "number",
        JTokenType.Boolean => "boolean",
        JTokenType.String => "string",
        _ => "object",
    };
}
