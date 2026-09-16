using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Security;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Routes a deserialized <see cref="InventorCommandEnvelope"/> to its handler, enforcing
/// read-only mode, the <c>send_code</c> opt-in gate, and the response-size guard, and
/// sanitizing any handler exception into an <c>API_ERROR</c>. Ported from nwd's CommandDispatcher.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly IReadOnlyDictionary<string, IInventorCommand> _commands;
    private readonly int _maxResponseBytes;

    public CommandDispatcher(IReadOnlyDictionary<string, IInventorCommand> commands, int maxResponseBytes)
    {
        _commands = commands;
        _maxResponseBytes = maxResponseBytes;
    }

    /// <summary>The registered command map, so the entrypoint can pass it into the context.</summary>
    public IReadOnlyDictionary<string, IInventorCommand> Commands => _commands;

    public InventorCommandResult Dispatch(InventorCommandContext ctx, InventorCommandEnvelope env)
    {
        var started = Stopwatch.StartNew();
        var meta = new InventorResponseMeta { TargetId = ctx.TargetId, InventorYear = ctx.InventorYear == 0 ? null : ctx.InventorYear };

        if (env.Command == "send_code" && !ctx.EnableSendCode)
            return InventorCommandResult.Fail(env.Id, InventorErrorCodes.SEND_CODE_DISABLED,
                "send_code is disabled. Enable it on the server (--enable-send-code) and the add-in (BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1).", meta);

        if (!_commands.TryGetValue(env.Command, out var cmd))
            return InventorCommandResult.Fail(env.Id, InventorErrorCodes.INVALID_ARGUMENT, $"unknown command: {env.Command}", meta);

        if (!cmd.IsReadOnly && ctx.ReadOnly)
            return InventorCommandResult.Fail(env.Id, InventorErrorCodes.READ_ONLY, $"{env.Command} is a write command and the server is read-only", meta);

        try
        {
            var result = cmd.Execute(ctx, env.Params ?? new JObject());
            Normalize(env, ctx, result, started);
            SanitizeResult(result);

            // F3-a: three-tier guard on the compact payload. Above the reject budget the call
            // fails with RESPONSE_TOO_LARGE + a narrowing hint; in the warning bands the hint is
            // attached to the response data as `size_warning` (handlers always return JObject).
            var decision = ResponseSizeGuard.Evaluate(env.Command, JsonConvert.SerializeObject(result.Data));
            if (decision.Reject)
                return InventorCommandResult.Fail(env.Id, InventorErrorCodes.RESPONSE_TOO_LARGE,
                    decision.RejectError ?? "response too large", result.Meta);
            if (decision.AgentWarning is not null && result.Data is JObject dataObj)
                dataObj["size_warning"] = decision.AgentWarning;

            // Final transport fence (5 MB in production).
            var serialized = JsonConvert.SerializeObject(result.Data);
            if (!ResponseSizeGuard.Check(serialized, _maxResponseBytes, out var sizeError))
                return InventorCommandResult.Fail(env.Id, sizeError!.Code, sizeError.Message, result.Meta);
            return result;
        }
        catch (Exception ex)
        {
            return InventorCommandResult.Fail(env.Id, InventorErrorCodes.API_ERROR, ErrorSanitizer.Sanitize(ex), meta);
        }
    }

    private static void Normalize(InventorCommandEnvelope env, InventorCommandContext ctx, InventorCommandResult result, Stopwatch started)
    {
        result.Id = env.Id;
        result.Meta.TargetId ??= ctx.TargetId;
        result.Meta.InventorYear ??= ctx.InventorYear == 0 ? null : ctx.InventorYear;
        result.Meta.ReadOnlyEnforced ??= ctx.ReadOnly;
        result.Meta.DurationMs = started.ElapsedMilliseconds;
    }

    private static void SanitizeResult(InventorCommandResult result)
    {
        if (result.Error is not null)
            result.Error.Message = ErrorSanitizer.Sanitize(result.Error.Message);

        if (result.Data is JObject obj)
            ErrorSanitizer.SanitizeErrorFields(obj);
    }
}
