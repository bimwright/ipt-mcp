#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>ilogic_list_rules</c> — read-only list of the iLogic rules stored in the target document
/// (<c>{name, is_active, is_codeblocks, scope:"internal"}</c>). External rules live in files and are
/// not enumerable through this API — <c>ilogic_run_external_rule</c> still resolves them by name.
/// Degrades to <c>UNSUPPORTED_HOST</c> when the iLogic add-in is absent (Inventor LT).
/// </summary>
public sealed class ILogicListRulesHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_list_rules";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var failure);
        if (failure != null) return failure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        var app = (Application)ctx.Application!;
        if (!ILogicBridge.TryGet(ctx, Name, app, out var ilogic, out failure))
            return failure!;

        try
        {
            var rules = new JArray();
            foreach (var r in ilogic!.GetRules(doc))
                rules.Add(new JObject
                {
                    ["name"] = r.Name,
                    ["is_active"] = r.IsActive,
                    ["is_codeblocks"] = r.IsCodeblocks,
                    ["scope"] = "internal",
                });
            return Ok(ctx, new JObject { ["count"] = rules.Count, ["rules"] = rules });
        }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
