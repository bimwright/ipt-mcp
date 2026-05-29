#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.ToolBaker;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// <c>run_baked_tool</c> — executes an accepted baked tool (preset = one read-only handler with fixed
/// args merged over runtime params; macro = a sequence). Every sub-command is re-checked against
/// <see cref="BakedToolDispatchAuthorizer"/> so a baked tool can never reach the platform layer
/// (<c>send_code</c>, <c>run_baked_tool</c>, etc.). Ported from nwd-mcp.
/// </summary>
public sealed class RunBakedToolHandler : IInventorCommand
{
    public string Name => "run_baked_tool";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };
        if (ctx.Commands == null)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.API_ERROR, "Command registry is not available for baked tool dispatch.", meta);

        var recordJson = p["tool_record"] as JObject;
        if (recordJson == null)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "tool_record is required.", meta);

        var record = recordJson.ToObject<BakedToolRecord>();
        if (record == null)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "tool_record is invalid.", meta);

        var validation = ToolCompiler.ValidateRecord(record);
        if (!validation.Ok)
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, validation.Error ?? "Baked tool failed validation.", meta);

        var runtimeParams = p["params"] as JObject ?? new JObject();
        if (string.Equals(record.Source, "preset", StringComparison.Ordinal))
        {
            return ExecuteOne(ctx, record.HandlerTool, Merge(ParseObject(record.FixedArgs), runtimeParams), meta);
        }

        var results = new JArray();
        foreach (var step in ParseArray(record.Sequence))
        {
            var command = CommandName(step);
            var stepParams = CommandParams(step);
            if (string.IsNullOrWhiteSpace(command))
                return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "Macro step is missing a command name.", meta);

            var result = ExecuteOne(ctx, command!, Merge(stepParams, runtimeParams), meta);
            if (!result.Ok)
                return result;
            results.Add(result.Data ?? JValue.CreateNull());
        }

        return InventorCommandResult.Success(Guid.Empty, new JObject
        {
            ["ok"] = true,
            ["tool_name"] = record.Name,
            ["results"] = results
        }, meta);
    }

    private static InventorCommandResult ExecuteOne(InventorCommandContext ctx, string command, JObject parameters, InventorResponseMeta meta)
    {
        if (!BakedToolDispatchAuthorizer.IsAllowed(command))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "Baked tool target is not allowed: " + command, meta);

        if (ctx.Commands == null || !ctx.Commands.TryGetValue(command, out var handler))
            return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "Baked tool target is not registered: " + command, meta);

        return handler.Execute(ctx, parameters);
    }

    private static JObject Merge(JObject baseArgs, JObject runtimeArgs)
    {
        var merged = (JObject)baseArgs.DeepClone();
        foreach (var property in runtimeArgs.Properties())
            merged[property.Name] = property.Value.DeepClone();
        return merged;
    }

    private static JObject ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JObject();
        return JObject.Parse(json);
    }

    private static JArray ParseArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JArray();
        return JArray.Parse(json);
    }

    private static string? CommandName(JToken step)
    {
        if (step is JObject obj)
            return (string?)obj["cmd"];
        return step.Value<string>();
    }

    private static JObject CommandParams(JToken step)
    {
        if (step is JObject obj)
            return obj["params"] as JObject ?? new JObject();
        return new JObject();
    }
}
#endif
