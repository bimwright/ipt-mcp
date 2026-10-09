#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>ilogic_upsert_rule</c> — create or replace an internal iLogic rule's VB text in the target
/// document. Existing rule → sets <c>iLogicRule.Text</c> without running (<c>auto_ran:false</c>);
/// new rule → <c>AddRule</c> compiles AND immediately runs it (<c>auto_ran:true</c> — iLogic API
/// behaviour). <c>run=true</c> runs an existing rule once; new rules are not executed twice.
/// </summary>
public sealed class ILogicUpsertRuleHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_upsert_rule";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var name = (string?)p["name"];
        var text = (string?)p["text"];
        if (string.IsNullOrWhiteSpace(name))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "name is required");
        if (text is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "text is required (VB rule source)");

        var doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var failure);
        if (failure != null) return failure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        var app = (Application)ctx.Application!;
        if (!ILogicBridge.TryGet(ctx, Name, app, out var ilogic, out failure))
            return failure!;

        var run = p["run"]?.Value<bool?>() == true;

        try
        {
            using var silent = SilentOperationScope.Enter(app, p);
            var data = new JObject { ["name"] = name, ["created"] = false, ["auto_ran"] = false, ["ran"] = false };
            return ILogicBridge.ExecuteRule(ctx, ilogic!, data, () =>
            {
                var existing = ilogic!.FindRule(doc, name!);
                data["effects_may_have_occurred"] = true;
                if (existing is not null)
                {
                    ilogic.SetRuleText(existing, text!);
                    if (!run) return null;
                    data["execution_attempted"] = true;
                    var status = ilogic.RunRuleDirect(existing, ILogicBridge.BuildArguments(app, p));
                    data["result"] = status;
                    data["ran"] = status == 0;
                    return status;
                }
                data["creation_attempted"] = true;
                data["execution_attempted"] = true;
                ilogic.AddRule(doc, name!, text!);
                data["created"] = true;
                data["auto_ran"] = true;
                data["ran"] = true;
                return null;
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
