#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Security;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>Executes only this invocation's temporary rule and restores its result channel.
/// Existing rules are never swept by name. Log markers are not an invocation-scoped result channel.</summary>
public sealed class ILogicRunSnippetHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_run_snippet";
    public bool IsReadOnly => false;
    internal const string TempRulePrefix = "TempMCPRule_";
    internal const string ResultPropSet = "Inventor User Defined Properties";
    internal const string ResultPropName = "MCP_RESULT";

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var code = (string?)p["code"];
        if (string.IsNullOrWhiteSpace(code))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "code is required (iLogic VB source)");
        var doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var failure);
        if (failure != null) return failure;
        if (doc is null) return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");
        var app = (Application)ctx.Application!;
        if (!ILogicBridge.TryGet(ctx, Name, app, out var ilogic, out failure)) return failure!;

        var ruleName = TempRulePrefix + Guid.NewGuid().ToString("N");
        var data = new JObject
        {
            ["rule"] = ruleName, ["ran"] = false, ["rule_deleted"] = true,
            ["residue_swept"] = 0, ["result"] = null,
        };
        ILogicResultChannel? channel = null;
        var ruleAttempted = false;
        try
        {
            using var silent = SilentOperationScope.Enter(app, p);
            return ILogicBridge.ExecuteRule(ctx, ilogic!, data, () =>
            {
                if (ilogic!.FindRule(doc, ruleName) is not null)
                    throw new InvalidOperationException("The temporary rule name already exists; no rule was overwritten.");
                channel = new ILogicResultChannel(new ResultStore(doc));
                channel.Begin();
                ruleAttempted = true;
                data["rule_deleted"] = false;
                data["execution_attempted"] = true;
                data["effects_may_have_occurred"] = true;
                ilogic.AddRule(doc, ruleName, code!);
                var result = channel.Read();
                data["result"] = result is null ? null : SecretMasker.Mask(result);
                data["result_present"] = result is not null;
                data["ran"] = true;
                return null;
            }, () =>
            {
                var errors = new List<string>();
                if (ruleAttempted)
                {
                    try
                    {
                        if (ilogic!.FindRule(doc, ruleName) is not null) ilogic.DeleteRule(doc, ruleName);
                        data["rule_deleted"] = ilogic.FindRule(doc, ruleName) is null;
                        if (data["rule_deleted"]?.Value<bool>() != true)
                            errors.Add("The invocation's temporary rule remains after cleanup.");
                    }
                    catch (Exception ex) { errors.Add("Temporary rule: " + ex.Message); }
                }
                try
                {
                    channel?.Restore();
                    data["result_property_restored"] = true;
                }
                catch (Exception ex)
                {
                    data["result_property_restored"] = false;
                    errors.Add("Result property: " + ex.Message);
                }
                if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            });
        }
        catch (Exception ex) { return ILogicOutcome.Complete(data, Meta(ctx), error: ex.Message); }
    }

    private sealed class ResultStore : ILogicResultStore
    {
        private readonly PropertySet _set;
        internal ResultStore(global::Inventor.Document doc)
        {
            _set = Handlers.Properties.PropertyAccess.FindSet(doc, ResultPropSet)
                ?? throw new InvalidOperationException("The document has no custom iProperty set.");
        }

        public bool TryRead(out object? value)
        {
            var property = Handlers.Properties.PropertyAccess.FindProperty(_set, ResultPropName);
            value = property?.Value;
            return property is not null;
        }
        public void Write(object value)
        {
            var property = Handlers.Properties.PropertyAccess.FindProperty(_set, ResultPropName);
            if (property is null) _set.Add(value, ResultPropName);
            else property.Value = value;
        }
        public void Delete()
            => Handlers.Properties.PropertyAccess.FindProperty(_set, ResultPropName)?.Delete();
    }
}
#endif
