#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>undo</c> — single Inventor undo step via the <c>AppUndoCmd</c> control definition (equivalent
/// to Ctrl+Z on the active document). <c>steps</c> (default 1, max 20) repeats it. Nothing to undo
/// is reported, not an error.
/// </summary>
public sealed class UndoHandler : HandlerBase, IInventorCommand
{
    public string Name => "undo";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var app = (Application)ctx.Application!;

        int steps;
        try { steps = (int?)p["steps"] ?? 1; }
        catch (Exception) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "steps must be an integer"); }
        if (steps < 1 || steps > 20)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "steps must be 1-20");

        try
        {
            ControlDefinition undo;
            try { undo = app.CommandManager.ControlDefinitions["AppUndoCmd"]; }
            catch { return Fail(ctx, InventorErrorCodes.UNSUPPORTED_HOST, "AppUndoCmd control definition not found"); }

            var done = 0;
            for (var i = 0; i < steps; i++)
            {
                if (!undo.Enabled) break;
                try { undo.Execute2(true); done++; }
                catch { break; }
            }
            return Ok(ctx, new JObject { ["undone"] = done, ["requested"] = steps });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
