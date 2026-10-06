#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>ilogic_get_rule</c> — read-only fetch of one internal rule's VB source plus its state flags.
/// <c>name</c> is required; a missing rule returns <c>INVALID_ARGUMENT</c>, not a generic API error.
/// </summary>
public sealed class ILogicGetRuleHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_get_rule";
    public bool IsReadOnly => true;

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
            var rule = ilogic!.FindRule(doc, name!);
            if (rule is null)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, $"no internal iLogic rule named '{name}' in the target document");
            return Ok(ctx, new JObject
            {
                ["name"] = name,
                ["scope"] = "internal",
                ["text"] = ilogic.GetRuleText(doc, name!),
            });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
