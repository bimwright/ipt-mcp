#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// Locates and activates the iLogic add-in (stable class id across Inventor 2022–2027), then wraps
/// its <c>Automation</c> object in the best available access layer: typed interop first, dynamic
/// fallback when the interop cannot load or the cast fails. Inventor LT ships without iLogic and a
/// security setting can leave the add-in blocked/unactivated — every failure degrades to
/// <c>UNSUPPORTED_HOST</c>/<c>API_ERROR</c>, never a raw exception.
/// </summary>
internal static class ILogicBridge
{
    internal const string AddInClassId = "{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}";

    internal static bool TryGet(
        InventorCommandContext ctx, string commandName, Application app,
        out ILogicAccess? access, out InventorCommandResult? failure)
    {
        access = null;
        failure = null;
        ILogicAssemblyResolver.EnsureInstalled();

        ApplicationAddIn? addIn;
        try { addIn = app.ApplicationAddIns.ItemById[AddInClassId]; }
        catch { addIn = null; }
        if (addIn is null)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.UNSUPPORTED_HOST,
                commandName + ": the iLogic add-in is not installed (Inventor LT has no iLogic)");
            return false;
        }

        try { if (!addIn.Activated) addIn.Activate(); }
        catch (Exception ex)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.API_ERROR,
                commandName + ": the iLogic add-in failed to activate — " + ex.Message);
            return false;
        }

        object? automation;
        try { automation = addIn.Automation; } catch { automation = null; }
        if (automation is null)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.UNSUPPORTED_HOST,
                commandName + ": the iLogic add-in exposes no Automation object");
            return false;
        }

#if INVENTOR_ILOGIC_TYPED
        try { access = new TypedILogicAccess(automation); return true; }
        catch { /* interop failed to load/cast — fall through to dynamic */ }
#endif
        access = new DynamicILogicAccess(automation);
        return true;
    }

    /// <summary>
    /// Drains <c>RuleExceptionsLog</c> into a JArray of <c>{rule, line, context, message}</c> and
    /// clears it — run handlers call this after the rule so stale entries never bleed into the next
    /// call. Secret-masked like log text (rule code may embed paths/values).
    /// </summary>
    internal static JArray DrainExceptions(ILogicAccess ilogic)
    {
        var arr = new JArray();
        foreach (var e in ilogic.GetExceptions())
            arr.Add(new JObject
            {
                ["rule"] = e.RuleName,
                ["line"] = e.LineNumber,
                ["context"] = e.Context,
                ["message"] = e.Message,
            });
        if (arr.Count > 0) ilogic.ClearExceptions();
        return arr;
    }

    /// <summary>Builds an Inventor <c>NameValueMap</c> from a flat JSON object for rule arguments.</summary>
    internal static object? BuildArguments(Application app, JObject? p)
    {
        if (p?["arguments"] is not JObject args || args.Count == 0) return null;
        var map = app.TransientObjects.CreateNameValueMap();
        foreach (var kv in args)
        {
            map.Add(kv.Key, kv.Value is null or { Type: JTokenType.Null }
                ? null
                : kv.Value.Type switch
                {
                    JTokenType.Boolean => kv.Value.Value<bool>(),
                    JTokenType.Integer => kv.Value.Value<long>(),
                    JTokenType.Float => kv.Value.Value<double>(),
                    _ => kv.Value.ToString(),
                });
        }
        return map;
    }
}
#endif
