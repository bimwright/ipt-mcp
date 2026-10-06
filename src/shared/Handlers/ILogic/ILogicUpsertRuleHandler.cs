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
/// behaviour, cannot be suppressed). <c>run=true</c> additionally runs the rule after upsert.
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
            var created = false;
            var autoRan = false;
            using var silent = SilentOperationScope.Enter(app, p);
            var prevSilent = ilogic!.SilentOperation;
            try
            {
                ilogic.SilentOperation = true;
                var existing = ilogic.FindRule(doc, name!);
                if (existing is not null)
                {
                    ilogic.SetRuleText(existing, text!);
                }
                else
                {
                    ilogic.AddRule(doc, name!, text!);
                    created = true;
                    autoRan = true; // AddRule compiles and runs once — iLogic API behaviour
                }
                if (run) ilogic.RunRule(doc, name!, ILogicBridge.BuildArguments(app, p));
            }
            finally { ilogic.SilentOperation = prevSilent; }

            return Ok(ctx, new JObject
            {
                ["name"] = name,
                ["created"] = created,
                ["auto_ran"] = autoRan,
                ["ran"] = run || autoRan,
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
