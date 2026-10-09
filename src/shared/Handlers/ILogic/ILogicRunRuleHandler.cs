#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>ilogic_run_rule</c> — run an internal iLogic rule already stored in the target document.
/// <c>name</c> required; optional flat <c>arguments</c> object becomes an Inventor NameValueMap for
/// <c>RunRuleWithArguments</c>. The rule runs with iLogic <c>SilentOperation</c> plus the standard
/// Inventor silent scope so compile/runtime dialogs cannot wedge the STA thread. Side effects are
/// the customer's rule's own — this is a write command.
/// </summary>
public sealed class ILogicRunRuleHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_run_rule";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var name = (string?)p["name"];
        if (string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "name is required");

        var doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var failure);
        if (failure != null) return failure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        var app = (Application)ctx.Application!;
        if (!ILogicBridge.TryGet(ctx, Name, app, out var ilogic, out failure))
            return failure!;

        try
        {
            var args = ILogicBridge.BuildArguments(app, p);
            using var silent = SilentOperationScope.Enter(app, p);
            var data = new JObject { ["rule"] = name, ["scope"] = "internal" };
            return ILogicBridge.ExecuteRule(ctx, ilogic!, data, () =>
            {
                data["execution_attempted"] = true;
                data["effects_may_have_occurred"] = true;
                var result = ilogic!.RunRule(doc, name!, args);
                data["result"] = result;
                data["ran"] = result == 0;
                return result;
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
