#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System.Collections.Generic;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// Minimal facade over the iLogic Automation object (<c>ApplicationAddIn.Automation</c>) so the
/// handlers never see whether calls went through the typed interop (<c>INVENTOR_ILOGIC_TYPED</c>)
/// or the <c>dynamic</c> fallback. Rule references are passed back as opaque tokens — only the
/// access layer unwraps them. <paramref name="args"/> parameters accept an Inventor
/// <c>NameValueMap</c> (or null).
/// </summary>
internal abstract class ILogicAccess
{
    internal sealed record RuleInfo(string Name, bool IsActive, bool IsCodeblocks);

    /// <summary>One entry from iLogic's structured <c>RuleExceptionsLog</c>.</summary>
    internal sealed record RuleExceptionInfo(string RuleName, int LineNumber, string? Context, string Message);

    /// <summary>Rules stored in the document (external rules are not listed).</summary>
    internal abstract IReadOnlyList<RuleInfo> GetRules(global::Inventor.Document doc);

    /// <summary>The named rule object, or null when absent.</summary>
    internal abstract object? FindRule(global::Inventor.Document doc, string name);

    internal abstract string? GetRuleText(global::Inventor.Document doc, string name);

    /// <summary>Compiles AND runs the rule text — iLogic's AddRule auto-runs on creation.</summary>
    internal abstract object AddRule(global::Inventor.Document doc, string name, string text);

    internal abstract int DeleteRule(global::Inventor.Document doc, string name);

    internal abstract int RunRule(global::Inventor.Document doc, string name, object? args);

    internal abstract int RunExternalRule(global::Inventor.Document doc, string name, object? args);

    internal abstract int RunRuleDirect(object rule, object? args);

    internal abstract void SetRuleText(object rule, string text);

    /// <summary><c>LogControl.FullFileName</c>, or null when the log is not file-backed.</summary>
    internal abstract string? LogFullFileName { get; }

    /// <summary>iLogic-side dialog suppression (independent of Inventor's SilentOperation).</summary>
    internal abstract bool SilentOperation { get; set; }

    /// <summary>
    /// Snapshot of <c>RuleExceptionsLog.Exceptions</c> — the only reliable structured channel when
    /// <c>Logger.Info</c> output is not file-backed on this machine (iLogicLogger.config-driven).
    /// Never throws: unavailable log → empty list.
    /// </summary>
    internal abstract IReadOnlyList<RuleExceptionInfo> GetExceptions();

    /// <summary>Clears <c>RuleExceptionsLog</c>; used by run handlers after draining entries.</summary>
    internal abstract void ClearExceptions();
}
#endif
