#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>ilogic_run_external_rule</c> — run a file-backed iLogic rule by name (iLogic resolves it via
/// the document's external-rule search order). Same argument/silent semantics as
/// <c>ilogic_run_rule</c>.
/// </summary>
public sealed class ILogicRunExternalRuleHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_run_external_rule";
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
            var prevSilent = ilogic!.SilentOperation;
            int result;
            try
            {
                ilogic.SilentOperation = true;
                result = ilogic.RunExternalRule(doc, name!, args);
            }
            finally { ilogic.SilentOperation = prevSilent; }

            return Ok(ctx, new JObject
            {
                ["rule"] = name,
                ["scope"] = "external",
                ["result"] = result,
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
