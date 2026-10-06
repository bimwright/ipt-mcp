#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System.Collections;
using System.Collections.Generic;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// Late-bound path over <c>addIn.Automation</c> — needs no <c>Autodesk.iLogic.*</c> reference at
/// runtime, which is what saves the plugin on Inventor LT-adjacent installs and private ALCs where
/// the interop never resolves. Every call can throw <c>RuntimeBinderException</c>; handlers funnel
/// that into <c>API_ERROR</c>/<c>UNSUPPORTED_HOST</c>.
/// </summary>
internal sealed class DynamicILogicAccess : ILogicAccess
{
    private readonly dynamic _a;

    internal DynamicILogicAccess(object automation) { _a = automation; }

    internal override IReadOnlyList<RuleInfo> GetRules(global::Inventor.Document doc)
    {
        var list = new List<RuleInfo>();
        if (_a.get_Rules(doc) is not IEnumerable rules) return list;
        foreach (dynamic r in rules)
            list.Add(new RuleInfo((string)r.Name, (bool)r.IsActive, (bool)r.IsCodeblocks));
        return list;
    }

    internal override object? FindRule(global::Inventor.Document doc, string name)
    {
        try { return _a.GetRule(doc, name); } catch { return null; }
    }

    internal override string? GetRuleText(global::Inventor.Document doc, string name)
    {
        try { return (string?)_a.GetRule(doc, name)?.Text; } catch { return null; }
    }

    internal override object AddRule(global::Inventor.Document doc, string name, string text)
        => _a.AddRule(doc, name, text)!;

    internal override int DeleteRule(global::Inventor.Document doc, string name)
        => (int)_a.DeleteRule(doc, name);

    internal override int RunRule(global::Inventor.Document doc, string name, object? args)
        => args is null ? (int)_a.RunRule(doc, name) : (int)_a.RunRuleWithArguments(doc, name, args);

    internal override int RunExternalRule(global::Inventor.Document doc, string name, object? args)
        => args is null ? (int)_a.RunExternalRule(doc, name) : (int)_a.RunExternalRuleWithArguments(doc, name, args);

    internal override int RunRuleDirect(object rule, object? args)
        => args is null ? (int)_a.RunRuleDirect(rule) : (int)_a.RunRuleDirectWithArguments(rule, args);

    internal override void SetRuleText(object rule, string text)
        => ((dynamic)rule).Text = text;

    internal override string? LogFullFileName
    {
        get { try { return (string?)_a.LogControl?.FullFileName; } catch { return null; } }
    }

    internal override bool SilentOperation
    {
        get => (bool)_a.SilentOperation;
        set => _a.SilentOperation = value;
    }

    internal override IReadOnlyList<RuleExceptionInfo> GetExceptions()
    {
        var list = new List<RuleExceptionInfo>();
        try
        {
            if (_a.RuleExceptionsLog?.Exceptions is not IEnumerable entries) return list;
            foreach (dynamic e in entries)
                list.Add(new RuleExceptionInfo((string)(e.RuleName ?? ""), (int)e.LineNumber,
                    (string?)e.Context?.ToString(), (string)(e.Message ?? "")));
        }
        catch { }
        return list;
    }

    internal override void ClearExceptions()
    {
        try { _a.RuleExceptionsLog?.Clear(); } catch { }
    }
}
#endif
