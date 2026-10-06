#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Security;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

/// <summary>
/// <c>ilogic_run_snippet</c> — run arbitrary iLogic VB <c>code</c> as a temporary internal rule.
/// Send-code-grade: only registered when <c>EnableSendCode</c> and additionally fenced in the
/// dispatcher. Uses the temp-rule pattern — <c>AddRule</c> compiles and runs, then
/// <c>try/finally DeleteRule</c>; before creating one it sweeps stale <c>TempMCPRule_*</c> residue
/// left by interrupted runs so agent VB never stays embedded in a customer document. Result
/// channel: the snippet returns a value via the iLogic-safe custom iProperty
/// <c>iProperties.Value("Custom", "MCP_RESULT") = "..."</c>, which this handler reads back and
/// deletes (iLogic's safety scanner blocks <c>System.IO</c>, so file transport is not an option).
/// Fallback: a <c>MCP_RESULT_BEGIN…MCP_RESULT_END</c> marker via <c>Logger.Info</c> is scraped when
/// this machine file-backs the iLogic log (<c>iLogicLogger.config</c>-dependent). Rule failures
/// surface as <c>exceptions</c> drained from iLogic's <c>RuleExceptionsLog</c>.
/// </summary>
public sealed class ILogicRunSnippetHandler : HandlerBase, IInventorCommand
{
    public string Name => "ilogic_run_snippet";
    public bool IsReadOnly => false;

    internal const string TempRulePrefix = "TempMCPRule_";
    internal const string ResultBegin = "MCP_RESULT_BEGIN";
    internal const string ResultEnd = "MCP_RESULT_END";
    internal const string ResultPropSet = "Inventor User Defined Properties";
    internal const string ResultPropName = "MCP_RESULT";
    private const int LogTailChars = 262144;
    private const int ResultMaxChars = 65536;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        var code = (string?)p["code"];
        if (string.IsNullOrWhiteSpace(code))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "code is required (iLogic VB source)");

        var doc = ActiveDocumentSupport.ResolveTarget(ctx, p, out var failure);
        if (failure != null) return failure;
        if (doc is null)
            return Fail(ctx, InventorErrorCodes.NO_DOCUMENT, "no active Inventor document");

        var app = (Application)ctx.Application!;
        if (!ILogicBridge.TryGet(ctx, Name, app, out var ilogic, out failure))
            return failure!;

        var ruleName = TempRulePrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
        var swept = 0;
        var deleted = false;
        try
        {
            using var silent = SilentOperationScope.Enter(app, p);
            var prevSilent = ilogic!.SilentOperation;
            try
            {
                ilogic.SilentOperation = true;

                // Residue sweep: a crashed run would otherwise leave agent VB embedded in the doc.
                foreach (var r in ilogic.GetRules(doc))
                {
                    if (!r.Name.StartsWith(TempRulePrefix, StringComparison.Ordinal)) continue;
                    try { ilogic.DeleteRule(doc, r.Name); swept++; } catch { }
                }

                ilogic.AddRule(doc, ruleName, code!); // AddRule compiles AND runs
            }
            finally
            {
                try { deleted = ilogic.DeleteRule(doc, ruleName) >= 0; } catch { }
                ilogic.SilentOperation = prevSilent;
            }

            return Ok(ctx, new JObject
            {
                ["ran"] = true,
                ["rule_deleted"] = deleted,
                ["residue_swept"] = swept,
                ["result"] = ReadResultProperty(doc) ?? ScrapeResultMarker(ilogic),
                ["exceptions"] = ILogicBridge.DrainExceptions(ilogic),
            });
        }
        catch (Exception ex)
        {
            // Even on failure, report whether the temp rule leaked so the agent can act on it.
            var err = Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message);
            if (err.Data is JObject d)
            {
                d["rule_deleted"] = deleted;
                d["residue_swept"] = swept;
                try { d["exceptions"] = ILogicBridge.DrainExceptions(ilogic!); } catch { }
            }
            return err;
        }
        finally
        {
            // Always sweep the result iProperty too — a snippet that sets it must not leak it
            // into a customer document.
            try { DeleteResultProperty(doc); } catch { }
        }
    }

    /// <summary>Reads + deletes the <c>MCP_RESULT</c> custom iProperty; null when unset.</summary>
    private static string? ReadResultProperty(global::Inventor.Document doc)
    {
        var prop = FindResultProperty(doc);
        if (prop is null) return null;
        try
        {
            var text = prop.Value?.ToString();
            try { prop.Delete(); } catch { }
            if (string.IsNullOrEmpty(text)) return null;
            if (text!.Length > ResultMaxChars) text = text.Substring(0, ResultMaxChars);
            return SecretMasker.Mask(text);
        }
        catch { return null; }
    }

    private static void DeleteResultProperty(global::Inventor.Document doc)
    {
        try { FindResultProperty(doc)?.Delete(); } catch { }
    }

    private static Property? FindResultProperty(global::Inventor.Document doc)
    {
        try
        {
            var set = Handlers.Properties.PropertyAccess.FindSet(doc, ResultPropSet);
            return set is null ? null : Handlers.Properties.PropertyAccess.FindProperty(set, ResultPropName);
        }
        catch { return null; }
    }

    /// <summary>Last <c>MCP_RESULT_BEGIN…MCP_RESULT_END</c> payload in the iLogic log tail, or null.</summary>
    private static string? ScrapeResultMarker(ILogicAccess ilogic)
    {
        try
        {
            var path = ilogic.LogFullFileName;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            string tail;
            using (var fs = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8, true))
            {
                var all = sr.ReadToEnd();
                tail = all.Length > LogTailChars ? all.Substring(all.Length - LogTailChars) : all;
            }
            var end = tail.LastIndexOf(ResultEnd, StringComparison.Ordinal);
            if (end < 0) return null;
            var begin = tail.LastIndexOf(ResultBegin, end, StringComparison.Ordinal);
            if (begin < 0) return null;
            begin += ResultBegin.Length;
            var payload = tail.Substring(begin, end - begin).Trim();
            return payload.Length == 0 ? null : SecretMasker.Mask(payload);
        }
        catch { return null; }
    }
}
#endif
