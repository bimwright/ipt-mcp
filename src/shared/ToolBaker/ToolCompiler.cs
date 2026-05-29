using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Shared.ToolBaker;

/// <summary>
/// Validates and smoke-tests baked-tool records: snake_case name, preset/macro source kind,
/// banned-API source policy, JSON params schema, and that every dispatched command is in the
/// allowed read-only set. API-agnostic; ported from nwd-mcp.
/// </summary>
public static class ToolCompiler
{
    private static readonly Regex ToolNamePattern = new Regex("^[a-z][a-z0-9_]{2,63}$", RegexOptions.Compiled);

    public static BakePolicyResult Validate(string source)
        => BakeCompilerPolicy.ValidateSource(source);

    public static BakePolicyResult ValidateRecord(BakedToolRecord record)
    {
        if (record == null)
        {
            return Fail("Baked tool record is required.");
        }

        if (!ToolNamePattern.IsMatch(record.Name ?? string.Empty))
        {
            return Fail("Baked tool name must use snake_case and start with a letter.");
        }

        if (!string.Equals(record.Source, "preset", StringComparison.Ordinal)
            && !string.Equals(record.Source, "macro", StringComparison.Ordinal))
        {
            return Fail("Baked tool source must be preset or macro.");
        }

        if (string.IsNullOrWhiteSpace(record.SourceCode))
        {
            return Fail("Baked tool source code is required.");
        }

        var policy = BakeCompilerPolicy.ValidateSource(record.SourceCode);
        if (!policy.Ok)
        {
            return policy;
        }

        if (!TryParseObject(record.ParamsSchema, out _))
        {
            return Fail("Baked tool params schema must be a JSON object.");
        }

        if (string.Equals(record.Source, "preset", StringComparison.Ordinal))
        {
            if (!BakedToolDispatchAuthorizer.IsAllowed(record.HandlerTool))
            {
                return Fail("Baked tool target is not allowed: " + record.HandlerTool);
            }
            var fixedArgs = ParseObject(record.FixedArgs, out var error);
            if (fixedArgs == null)
            {
                return Fail(error ?? "Invalid fixed args");
            }
        }
        else
        {
            var sequence = ParseArray(record.Sequence, out var error);
            if (sequence == null)
            {
                return Fail(error ?? "Invalid sequence");
            }

            foreach (var step in sequence)
            {
                var commandName = CommandName(step);
                if (commandName == null || !BakedToolDispatchAuthorizer.IsAllowed(commandName))
                {
                    return Fail("Baked tool target is not allowed: " + commandName);
                }
            }
        }

        return new BakePolicyResult { Ok = true };
    }

    public static BakePolicyResult CompileAndSmokeTest(BakedToolRecord record, Func<string, JToken, BakePolicyResult> preflight)
    {
        var validation = ValidateRecord(record);
        if (!validation.Ok)
        {
            return validation;
        }

        if (preflight == null)
        {
            return validation;
        }

        if (string.Equals(record.Source, "preset", StringComparison.Ordinal))
        {
            var preflightResult = preflight(record.HandlerTool, ParseObject(record.FixedArgs, out _) ?? new JObject());
            return preflightResult.Ok
                ? validation
                : Fail("Baked tool smoke test failed: " + preflightResult.Error);
        }

        foreach (var step in ParseArray(record.Sequence, out _) ?? new JArray())
        {
            var commandName = CommandName(step);
            var commandParams = CommandParams(step);
            if (commandName == null || commandParams == null)
            {
                return Fail("Macro baked tool steps must include a command name and params for smoke testing.");
            }

            var preflightResult = preflight(commandName, commandParams);
            if (!preflightResult.Ok)
            {
                return Fail("Baked tool smoke test failed: " + preflightResult.Error);
            }
        }

        return validation;
    }

    private static JObject? ParseObject(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JObject();
        }

        try
        {
            return JObject.Parse(json);
        }
        catch (Exception ex)
        {
            error = "Baked tool fixed args must be a JSON object: " + ex.Message;
            return null;
        }
    }

    private static bool TryParseObject(string json, out JObject? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            value = new JObject();
            return true;
        }

        try
        {
            value = JObject.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JArray? ParseArray(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JArray();
        }

        try
        {
            return JArray.Parse(json);
        }
        catch (Exception ex)
        {
            error = "Baked tool sequence must be a JSON array: " + ex.Message;
            return null;
        }
    }

    private static string? CommandName(JToken step)
    {
        if (step is JObject obj)
        {
            return (string?)obj["cmd"];
        }

        return step?.Value<string>();
    }

    private static JToken? CommandParams(JToken step)
    {
        if (step is JObject obj)
        {
            return obj["params"] ?? new JObject();
        }

        return null;
    }

    private static BakePolicyResult Fail(string error)
        => new BakePolicyResult { Ok = false, Error = error };
}
