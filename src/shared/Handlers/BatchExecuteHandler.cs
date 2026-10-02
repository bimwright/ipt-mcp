#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// <c>batch_execute</c> — run up to <see cref="BatchExecutor.MaxCommands"/> wire commands inside one
/// Inventor transaction (single undo step), so modeling loops like work-plane→sketch→draw→close→extrude
/// cost one round-trip instead of six (spec F4-P1, B2 granularity). Iteration + stop-at-first-error
/// logic lives in <see cref="BatchExecutor"/> for unit testing; this handler owns the transaction and
/// the per-sub-command dispatch through <c>ctx.Commands</c>, re-checking read-only per sub-command.
/// </summary>
public sealed class BatchExecuteHandler : HandlerBase, IInventorCommand
{
    public string Name => "batch_execute";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.Commands == null)
            return Fail(ctx, InventorErrorCodes.API_ERROR, "command registry is not available for batch dispatch");

        if (p["commands"] is not JArray commands || commands.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "commands must be a non-empty array of {command, params}");

        if (commands.Count > BatchExecutor.MaxCommands)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, BatchExecutor.TooManyCommandsMessage(commands.Count));

        var app = (Application)ctx.Application!;
        if (app.ActiveDocument is not { } doc)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no document is open");

        bool continueOnError;
        try { continueOnError = (bool?)p["continue_on_error"] ?? false; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "continue_on_error must be a boolean"); }

        // No current handler opens its own transaction, but Inventor End/Abort fail when the
        // transaction is not current — a future handler that leaks a child tx would wedge this
        // wrapper, so keep transactions out of sub-handlers.
        Transaction? tx = null;
        try
        {
            tx = app.TransactionManager.StartTransaction(doc, "MCP: batch_execute");

            var outcome = BatchExecutor.Run(commands, continueOnError, (cmdName, subParams) =>
            {
                if (!ctx.Commands.TryGetValue(cmdName, out var handler))
                    return BatchExecutor.StepResult.Failure($"unknown command: {cmdName}");
                if (ctx.ReadOnly && !handler.IsReadOnly)
                    return BatchExecutor.StepResult.Failure($"{cmdName} is a write command and the session is read-only");

                var r = handler.Execute(ctx, subParams);
                return r.Ok
                    ? BatchExecutor.StepResult.Success(r.Data)
                    : BatchExecutor.StepResult.Failure($"{r.Error?.Code}: {r.Error?.Message}");
            });

            var rolledBack = outcome.AnyFailed && !continueOnError;
            if (rolledBack) tx.Abort();
            else tx.End();
            tx = null;

            // Spill parity with run_baked_tool: 20 heavy read steps can exceed the inline
            // budget — results over 64 KiB go to a spill file instead of tripping the outer
            // RESPONSE_TOO_LARGE after the transaction already committed.
            var data = new JObject
            {
                ["executed"] = outcome.Results.Count,
                ["rolled_back"] = rolledBack,
            };
            ResponseSpillWriter.AttachResults("batch_execute", data, outcome.Results, ResponseSpillWriterFactory.ForContext(ctx));
            return Ok(ctx, data);
        }
        catch (Exception ex)
        {
            try { tx?.Abort(); } catch { }
            return Fail(ctx, InventorErrorCodes.API_ERROR, "batch_execute failed: " + ex.Message);
        }
    }
}
#endif
