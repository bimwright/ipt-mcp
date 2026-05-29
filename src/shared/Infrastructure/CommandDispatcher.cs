using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Security;

namespace Bimwright.Inventor.Shared.Infrastructure;

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

        if (!_commands.TryGetValue(env.Command, out var cmd))
            return InventorCommandResult.Fail(env.Id, InventorErrorCodes.INVALID_ARGUMENT, $"unknown command: {env.Command}", meta);

        if (!cmd.IsReadOnly && ctx.ReadOnly)
            return InventorCommandResult.Fail(env.Id, InventorErrorCodes.READ_ONLY, $"{env.Command} is a write command and the server is read-only", meta);

        if (env.Command == "send_code" && !ctx.EnableSendCode)
            return InventorCommandResult.Fail(env.Id, InventorErrorCodes.SEND_CODE_DISABLED,
                "send_code is disabled. Enable it on the server (--enable-send-code) and the add-in (BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1).", meta);

        try
        {
            var result = cmd.Execute(ctx, env.Params ?? new JObject());
            Normalize(env, ctx, result, started);
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
        result.Meta.DurationMs = started.ElapsedMilliseconds;
    }
}
