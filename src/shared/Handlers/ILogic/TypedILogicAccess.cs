#if INVENTOR_ILOGIC_TYPED
using System.Collections.Generic;
using Autodesk.iLogic.Automation;
using Autodesk.iLogic.Interfaces;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// Typed path over <see cref="iLogicAutomation"/>. Kept in its own class behind
/// <c>INVENTOR_ILOGIC_TYPED</c>: the assembly references load lazily at first JIT of this type,
/// so <see cref="ILogicBridge"/> can try/catch a failed load and fall back to
/// <see cref="DynamicILogicAccess"/>.
/// </summary>
internal sealed class TypedILogicAccess : ILogicAccess
{
    private readonly iLogicAutomation _a;

    internal TypedILogicAccess(object automation) { _a = (iLogicAutomation)automation; }

    internal override IReadOnlyList<RuleInfo> GetRules(global::Inventor.Document doc)
    {
        var list = new List<RuleInfo>();
        if (_a.get_Rules(doc) is not System.Collections.IEnumerable rules) return list;
        foreach (iLogicRule r in rules)
            list.Add(new RuleInfo(r.Name, r.IsActive, r.IsCodeblocks));
        return list;
    }

    internal override object? FindRule(global::Inventor.Document doc, string name)
        => _a.GetRule(doc, name);

    internal override string? GetRuleText(object rule)
        => ((iLogicRule)rule).Text;

    internal override object AddRule(global::Inventor.Document doc, string name, string text)
        => _a.AddRule(doc, name, text)!;

    internal override int DeleteRule(global::Inventor.Document doc, string name)
        => _a.DeleteRule(doc, name);

    internal override int RunRule(global::Inventor.Document doc, string name, object? args)
        => args is null ? _a.RunRule(doc, name) : _a.RunRuleWithArguments(doc, name, (NameValueMap)args);

    internal override int RunExternalRule(global::Inventor.Document doc, string name, object? args)
        => args is null ? _a.RunExternalRule(doc, name) : _a.RunExternalRuleWithArguments(doc, name, (NameValueMap)args);

    internal override int RunRuleDirect(object rule, object? args)
        => args is null ? _a.RunRuleDirect((iLogicRule)rule) : _a.RunRuleDirectWithArguments((iLogicRule)rule, (NameValueMap)args);

    internal override void SetRuleText(object rule, string text)
        => ((iLogicRule)rule).Text = text;

    internal override string? LogFullFileName
    {
        get { try { return _a.LogControl?.FullFileName; } catch { return null; } }
    }

    internal override bool SilentOperation
    {
        get => _a.SilentOperation;
        set => _a.SilentOperation = value;
    }

    internal override IReadOnlyList<RuleExceptionInfo> GetExceptions()
    {
        var list = new List<RuleExceptionInfo>();
        try
        {
            if (_a.RuleExceptionsLog?.Exceptions is not System.Collections.IEnumerable entries) return list;
            foreach (ExceptionLogEntry e in entries)
                list.Add(new RuleExceptionInfo(e.RuleName ?? "", e.LineNumber, e.Context.ToString(), e.Message ?? ""));
        }
        catch { throw; }
        return list;
    }

    internal override void ClearExceptions()
    {
        _a.RuleExceptionsLog?.Clear();
    }
}
#endif
