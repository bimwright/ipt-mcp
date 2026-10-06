#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>run_command</c> — execute an Inventor control definition by internal name
/// (<c>Execute2(ExecuteOnDone:true)</c>). <c>name</c> must be an internal name from
/// <c>list_commands</c>; unknown names return <c>INVALID_ARGUMENT</c>. Runs under the standard
/// silent scope — many commands are modal by nature, so pass <c>silent=true</c> when automating
/// dialogs-prone commands.
/// </summary>
public sealed class RunCommandHandler : HandlerBase, IInventorCommand
{
    public string Name => "run_command";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var name = (string?)p["name"];
        if (string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "name is required (internal command name from list_commands)");

        var app = (Application)ctx.Application!;
        try
        {
            ControlDefinition def;
            try { def = app.CommandManager.ControlDefinitions[name!]; }
            catch { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"unknown control definition '{name}'"); }

            using var silent = SilentOperationScope.Enter(app, p);
            def.Execute2(true);
            return Ok(ctx, new JObject { ["command"] = name, ["executed"] = true });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
